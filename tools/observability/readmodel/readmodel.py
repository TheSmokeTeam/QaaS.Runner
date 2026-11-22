#!/usr/bin/env python3
"""Materialize bounded Runner terminal KPIs from an accepted-only export and independent files."""
import argparse
from calendar import timegm
from collections import Counter
from datetime import datetime
from itertools import product
import json
from math import ceil
import os
from pathlib import Path
import re
import stat
import tempfile

DAY = 86400 * 10_000_000
OPERATIONS = ("run", "act", "assert")
OUTCOMES = ("pass", "assertion_failure", "infra_failure", "cancelled", "completed", "unknown")
EVALUABLE = {"pass", "assertion_failure", "infra_failure", "completed"}
GOOD = {"pass", "assertion_failure", "completed"}
MAX_INPUT_BYTES = 64 * 1024 * 1024
MAX_MANIFEST_BYTES = 24 * 1024 * 1024
MAX_LINE = 2048
MAX_ROWS = 100_000
MAX_PROJECTS = 5_000
KEY = re.compile(r"[0-9a-f]{32}")
LABEL = re.compile(r"[a-z][a-z0-9_-]{0,31}")
SOURCE = re.compile(r"[A-Za-z][A-Za-z0-9_.-]{0,63}")
UTC = re.compile(r"(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d)(?:\.(\d{1,7}))?Z", re.ASCII)

METRICS = {
    "adoption_ratio": "Observed active eligible projects divided by independently eligible projects.",
    "active_projects": "Current eligible projects with at least one in-window terminal.",
    "eligible_projects": "Current cohort in the independent project roster.",
    "enrolled_projects": "Current eligible cohort independently marked enrolled.",
    "attempts": "Distinct in-window terminal attempts of current eligible projects.",
    "throughput_per_day": "Distinct eligible terminal attempts per exact UTC day.",
    **{f"operation_{op}_attempts": f"Terminal attempts explicitly reporting the {op} operation." for op in OPERATIONS},
    **{name: f"Distinct received terminal attempts with outcome {name}." for name in OUTCOMES},
    "functional_evaluable": "Received pass or assertion failure attempts.",
    "functional_failure_ratio": "Assertion failures divided by received pass and assertion failure attempts.",
    "functional_pass_ratio": "Pass divided by received pass and assertion failure attempts.",
    "infra_evaluable": "Received pass, assertion failure, infrastructure failure or completed attempts.",
    "infra_failure_ratio": "Infrastructure failures divided by known noncancelled terminal outcomes.",
    "duration_samples": "Known noncancelled terminal durations.",
    "duration_p50_seconds": "Nearest-rank p50 in seconds across known noncancelled terminal durations.",
    "duration_p95_seconds": "Nearest-rank p95 in seconds across known noncancelled terminal durations.",
    "ledger_available": "One when an independent expected-attempt ledger was provided.",
    "expected": "Mature independently expected in-window attempts.",
    "covered": "Mature expected attempts with a matching in-window terminal.",
    "missing": "Mature independent due attempts without a matching in-window terminal.",
    "unexpected": "In-window terminals without an in-window ledger due attempt.",
    "pending_maturity_observed": "Observed attempts with a matching in-window due record not yet mature at cutoff.",
    "platform_good": "Mature expected attempts matched to pass, assertion failure or successful completion.",
    "platform_good_ratio": "Platform-good matched attempts divided by mature independently expected attempts.",
    "coverage_ratio": "Mature expected attempts with an observed terminal divided by mature expected attempts.",
    "unrostered_attempts": "In-window terminals lacking any roster entry; aggregate scope only.",
    "out_of_cohort_attempts": "In-window terminal attempts with roster entry outside current eligibility; aggregate scope only.",
    "as_of_timestamp_seconds": "Exclusive fixed UTC model cutoff. Old values indicate stale materialization.",
    "window_start_timestamp_seconds": "Inclusive fixed UTC window start.",
}


def tick(value):
    match = UTC.fullmatch(value) if isinstance(value, str) else None
    if not match:
        raise ValueError("invalid UTC timestamp")
    try:
        exact = datetime.fromisoformat(match[1])
        if exact.year < 1970:
            raise ValueError
        return timegm(exact.utctimetuple()) * 10_000_000 + int((match[2] or "").ljust(7, "0"))
    except (ValueError, OverflowError):
        raise ValueError("invalid UTC timestamp") from None


def opaque(value):
    if not isinstance(value, str) or not KEY.fullmatch(value) or value == "0" * 32:
        raise ValueError("invalid opaque identity")
    return value


def object_shape(value, fields, group):
    if not isinstance(value, dict) or set(value) != set(fields):
        raise ValueError(f"invalid {group} fields")
    return value


def shape_v1(value):
    if type(value["schema_version"]) is not int or value["schema_version"] != 1:
        raise ValueError("unsupported schema version")


def int_in(value, low, high):
    return type(value) is int and low <= value <= high


def enum(value, allowed, name):
    if not isinstance(value, str) or value not in allowed:
        raise ValueError(f"invalid {name}")
    return value


def no_duplicate_keys(items):
    result = dict(items)
    if len(result) != len(items):
        raise ValueError("duplicate JSON keys")
    return result


def no_constants(value):
    raise ValueError("nonstandard JSON value")


def decode(raw):
    try:
        return json.loads(raw.decode("utf8"), object_pairs_hook=no_duplicate_keys, parse_constant=no_constants)
    except (ValueError, RecursionError, OverflowError):
        raise ValueError("invalid or ambiguous JSON") from None


def open_limited(path, limit):
    # Regular files only; nonblocking open cannot hang on a substituted FIFO.
    fd = os.open(path, os.O_RDONLY | getattr(os, "O_NONBLOCK", 0) | getattr(os, "O_NOFOLLOW", 0))
    try:
        data = os.fstat(fd)
        if not stat.S_ISREG(data.st_mode) or data.st_size > limit:
            raise ValueError("input type or size exceeds policy")
        return os.fdopen(fd, "rb")
    except BaseException:
        os.close(fd)
        raise


def file_stamp(source):
    file = os.fstat(source.fileno())
    return file.st_size, file.st_mtime_ns


def read_manifest(path):
    with open_limited(path, MAX_MANIFEST_BYTES) as source:
        stamp = file_stamp(source)
        content = source.read(MAX_MANIFEST_BYTES + 1)
        if len(content) > MAX_MANIFEST_BYTES:
            raise ValueError("manifest exceeds size policy")
        if stamp != file_stamp(source):
            raise ValueError("manifest changed during read; use an immutable snapshot")
        return decode(content)


def independent(data, name, cutoff):
    shape_v1(data)
    identifier = data[name]
    if not isinstance(identifier, str) or not SOURCE.fullmatch(identifier):
        raise ValueError("invalid independent snapshot identifier")
    low, high = tick(data["valid_from"]), tick(data["valid_until"])
    if not low <= cutoff - 30 * DAY < cutoff < high:
        raise ValueError("independent snapshot does not cover the 30-day report")
    return identifier


def allowed_labels(value, limit):
    if not isinstance(value, list) or not 1 <= len(value) <= limit or any(
        not isinstance(label, str) or label == "all" or not LABEL.fullmatch(label) for label in value
    ) or len(set(value)) != len(value):
        raise ValueError("invalid or excessive scope allowlist")
    return ("all", *sorted(value))


def roster(path, cutoff):
    data = object_shape(read_manifest(path),
        ("schema_version","roster_id","valid_from","valid_until","teams","environments","projects"),"roster")
    independent(data,"roster_id",cutoff)
    teams, environments = allowed_labels(data["teams"],8), allowed_labels(data["environments"],4)
    projects = data["projects"]
    if not isinstance(projects,list) or len(projects) > MAX_PROJECTS:
        raise ValueError("invalid or oversized project roster")
    result = {}
    for item in projects:
        r = object_shape(item,("project_key","team","environment","eligible_from","ineligible_at","enrolled"),"project roster")
        key = opaque(r["project_key"])
        team = enum(r["team"],teams[1:],"roster team")
        env = enum(r["environment"],environments[1:],"roster environment")
        low, high = tick(r["eligible_from"]), tick(r["ineligible_at"]) if r["ineligible_at"] is not None else None
        if key in result or type(r["enrolled"]) is not bool or high is not None and high <= low:
            raise ValueError("duplicate project or invalid eligibility attributes")
        result[key] = (team,env,low,high,r["enrolled"])
    return result, teams, environments


def ledger(path, cutoff, projects):
    if path is None:
        return None
    data = object_shape(read_manifest(path),("schema_version","ledger_id","valid_from","valid_until","attempts"),"ledger")
    independent(data,"ledger_id",cutoff)
    attempts = data["attempts"]
    if not isinstance(attempts,list) or len(attempts) > MAX_ROWS:
        raise ValueError("invalid or oversized expected ledger")
    result = {}
    for item in attempts:
        a = object_shape(item,("attempt_id","project_key","due_at","mature_at"),"ledger record")
        key,project = opaque(a["attempt_id"]),opaque(a["project_key"])
        due,mature = tick(a["due_at"]),tick(a["mature_at"])
        if key in result or project not in projects or mature < due:
            raise ValueError("duplicate, unrostered or invalid expected attempt")
        result[key]=(project,due,mature)
    return result


def terminals(path, expected):
    result = {}
    total = 0
    with open_limited(path,MAX_INPUT_BYTES) as source:
        stamp = file_stamp(source)
        for number in range(1, MAX_ROWS+2):
            raw = source.readline(MAX_LINE+1)
            if not raw:
                if stamp != file_stamp(source):
                    raise ValueError("terminal export changed during read; use an immutable snapshot")
                return result
            total += len(raw)
            if number > MAX_ROWS or len(raw) > MAX_LINE or total > MAX_INPUT_BYTES:
                raise ValueError("terminal export exceeds row or byte policy")
            try:
                a = object_shape(decode(raw),("schema_version","attempt_id","project_key","operation",
                    "outcome","completed_at","duration_ms","exit_code"),"terminal")
                shape_v1(a)
                key, project = opaque(a["attempt_id"]),opaque(a["project_key"])
                op = enum(a["operation"],OPERATIONS,"operation")
                outcome = enum(a["outcome"],OUTCOMES,"outcome")
                duration, exit_code = a["duration_ms"], a["exit_code"]
                if not int_in(duration,0,2**63-1) or exit_code is not None and not int_in(exit_code,-2**31,2**31-1):
                    raise ValueError("invalid duration or exit code")
                record = (project,tick(a["completed_at"]),op,outcome,duration,exit_code)
                if expected is not None and key in expected and expected[key][0]!=project:
                    raise ValueError("terminal and expected ledger project conflict")
                if key in result and record != result[key]:
                    raise ValueError("conflicting terminal replay")
                result[key] = record
            except ValueError as ex:
                raise ValueError(f"terminal input line {number}: {ex}") from None
    return result


def ratio(x,y):
    return x/y if y else None


def series(cutoff,projects,teams,envs,events,expected):
    current = {k:r for k,r in projects.items() if r[2] <= cutoff and (r[3] is None or cutoff < r[3])}
    # Index data by the 4 allowed aggregate scopes for each eligible item; work is
    # bounded by selected rows, not by rows multiplied by all configured scopes.
    for days in (7,30):
        start = cutoff - days * DAY
        per_scope = {scope: dict(projects=set(), obs={}, due={}, pending=set())
                     for scope in product(teams,envs)}
        def scopes(team,env):return (("all","all"),("all",env),(team,"all"),(team,env))
        for key,(team,env,low,_,enabled) in current.items():
            for scope in scopes(team,env):
                per_scope[scope]["projects"].add(key)
        unknown = outside = 0
        for identity,ev in events.items():
            key,ts,*_ = ev
            if not start <= ts < cutoff:
                continue
            if key not in projects:
                unknown += 1
            elif key not in current or ts < current[key][2]:
                outside += 1
            else:
                for scope in scopes(*current[key][:2]):
                    per_scope[scope]["obs"][identity]=ev
        if expected is not None:
            for identity,(key,due,mature) in expected.items():
                if key not in current or not max(start,current[key][2]) <= due < cutoff:
                    continue
                for scope in scopes(*current[key][:2]):
                    group = per_scope[scope]
                    if mature < cutoff:
                        group["due"][identity]=(key,due,mature)
                    else:
                        group["pending"].add(identity)
        for (team,env),scope in per_scope.items():
            obs,due,pending = scope["obs"],scope["due"],scope["pending"]
            cohort=scope["projects"]
            observed_keys=set(obs)
            active=len({r[0] for r in obs.values()})
            statuses=Counter(r[3] for r in obs.values())
            ops=Counter(r[2] for r in obs.values())
            durations=sorted(r[4] for r in obs.values() if r[3] in EVALUABLE)
            n=len(durations)
            functional=statuses["pass"]+statuses["assertion_failure"]
            match=observed_keys & due.keys()
            good=sum(obs[k][3] in GOOD for k in match)
            values={
                "adoption_ratio":ratio(active,len(cohort)), "active_projects":active,"eligible_projects":len(cohort),
                "enrolled_projects":sum(projects[k][4] for k in cohort), "attempts":len(obs),
                "throughput_per_day":len(obs)/days,**{f"operation_{op}_attempts":ops[op] for op in OPERATIONS},
                **{status:statuses[status] for status in OUTCOMES}, "functional_evaluable":functional,
                "functional_failure_ratio":ratio(statuses["assertion_failure"],functional),
                "functional_pass_ratio":ratio(statuses["pass"],functional),
                "infra_evaluable":n,"infra_failure_ratio":ratio(statuses["infra_failure"],n),"duration_samples":n,
                "duration_p50_seconds":durations[ceil(.5*n)-1]/1000 if n else None,
                "duration_p95_seconds":durations[ceil(.95*n)-1]/1000 if n else None,
                "ledger_available":int(expected is not None),"expected":len(due),"covered":len(match),
                "missing":len(due.keys()-match),"unexpected":len(observed_keys-due.keys()-pending),
                "pending_maturity_observed":len(observed_keys & pending),"platform_good":good,
                "platform_good_ratio":ratio(good,len(due)),"coverage_ratio":ratio(len(match),len(due)),
                "unrostered_attempts":unknown if team == env == "all" else None,
                "out_of_cohort_attempts":outside if team == env == "all" else None,
                "as_of_timestamp_seconds":cutoff / 10_000_000,"window_start_timestamp_seconds":start / 10_000_000,
            }
            if expected is None:
                for k in ("expected","covered","missing","unexpected","pending_maturity_observed",
                          "platform_good","platform_good_ratio","coverage_ratio"):
                    values[k] = None
            assert values.keys() == METRICS.keys()
            yield (f"{days}d",team,env),values


def exposition(rows):
    output = []
    for metric,help_text in METRICS.items():
        name="qaas_runner_kpi_"+metric
        output.extend((f"# HELP {name} {help_text}",f"# TYPE {name} gauge"))
        for (window,team,env),value in rows:
            if value[metric] is not None:
                output.append(f'{name}{{window="{window}",team="{team}",environment="{env}"}} {value[metric]}')
    return "\n".join(output)+"\n"


def atomic(path, payload):
    with tempfile.NamedTemporaryFile(mode="w",dir=path.parent, prefix=".qaas-kpi-", suffix=".tmp",
                                     encoding="utf8",newline="\n",delete=False) as output:
        candidate=Path(output.name)
        try:
            output.write(payload)
            output.flush()
            os.fsync(output.fileno())
            os.replace(candidate,path)
        finally:
            candidate.unlink(missing_ok=True)


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--accepted",type=Path,required=True,help="Immutable authorized accepted-only event JSONL (not pending spool)")
    p.add_argument("--roster",type=Path,required=True)
    p.add_argument("--ledger",type=Path)
    p.add_argument("--as-of",required=True,help="Exclusive UTC snapshot cutoff")
    p.add_argument("--output",type=Path,help="Atomically replace this 0600 output metric file; default stdout")
    a=p.parse_args()
    try:
        cutoff=tick(a.as_of)
        if a.output and any(a.output.resolve()==source.resolve()
                            for source in (a.accepted,a.roster,a.ledger) if source is not None):
            raise ValueError("output must differ from all inputs")
        projects,teams,envs=roster(a.roster,cutoff)
        expected=ledger(a.ledger,cutoff,projects)
        events=terminals(a.accepted,expected)
        snapshot=exposition(list(series(cutoff,projects,teams,envs,events,expected)))
        if a.output:
            atomic(a.output,snapshot)
        else:
            print(snapshot,end="")
    except ValueError as e:
        p.exit(2,f"invalid read-model input: {e}\n")
    except OSError:
        p.exit(2,"unable to read input or publish output; verify file paths and access\n")


if __name__=="__main__":
    main()
