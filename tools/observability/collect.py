#!/usr/bin/env python3
"""Collect one bounded batch of QaaS Runner attempts without involving the Runner."""
import argparse
import errno
from datetime import datetime
import fcntl
import hashlib
import http.client
import json
import os
from pathlib import Path
import re
import signal
import stat
import sys
from urllib.parse import urlsplit


HEX = re.compile(r"[0-9a-f]{32}")
FILENAME = re.compile(r"[0-9a-f]{32}\.json")
INDEX = re.compile(r"qaas-attempts-v1-[a-z0-9][a-z0-9-]{0,62}")
UTC = re.compile(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z")
FIELDS = {"schema_version", "attempt_id", "project_key", "operation", "outcome",
          "completed_at", "duration_ms", "exit_code"}
OPERATIONS = ("run", "act", "assert")
OUTCOMES = ("pass", "assertion_failure", "infra_failure", "cancelled", "completed", "unknown")
MAX_RECORD_BYTES = 1024
MAX_RESPONSE_BYTES = 1024 * 1024


class Retry(Exception):
    """Indeterminate transport or response; preserve the source summaries."""


class Deadline(Retry):
    """Pass-wide deadline; never swallowed by per-item replay handling."""


def encode(value):
    return json.dumps(value, ensure_ascii=True, sort_keys=True,
                      separators=(",", ":"), allow_nan=False).encode("ascii")


def unique_keys(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate JSON key")
        result[key] = value
    return result


def parse(value):
    return json.loads(value.decode("utf-8"), object_pairs_hook=unique_keys)


def checked(value, name):
    if not isinstance(value, dict) or value.keys() != FIELDS:
        raise ValueError("invalid summary shape")
    for key in ("attempt_id", "project_key"):
        token = value[key]
        if not isinstance(token, str) or not HEX.fullmatch(token) or token == "0"*32:
            raise ValueError("invalid summary identity")
    if name != value["attempt_id"] + ".json":
        raise ValueError("summary filename does not match attempt")
    if type(value["schema_version"]) is not int or value["schema_version"] != 1:
        raise ValueError("unknown schema version")
    if value["operation"] not in OPERATIONS or value["outcome"] not in OUTCOMES:
        raise ValueError("invalid operation/outcome")
    timestamp = value["completed_at"]
    if not isinstance(timestamp, str) or not UTC.fullmatch(timestamp):
        raise ValueError("expected UTC RFC3339 timestamp")
    datetime.fromisoformat(timestamp.replace("Z", "+00:00"))
    duration, code = value["duration_ms"], value["exit_code"]
    if type(duration) is not int or not 0 <= duration < 2**63:
        raise ValueError("invalid duration")
    if code is not None and (type(code) is not int or not -(2**31) <= code < 2**31):
        raise ValueError("invalid exit code")
    return value


def _same_json_types(left, right):
    return isinstance(left, dict) and all(type(left[k]) is type(v) for k, v in right.items())


def identity(value):
    return hashlib.sha256(
        f'qaas-attempt-summary-v1:{value["project_key"]}:{value["attempt_id"]}'.encode("ascii")
    ).hexdigest()


def read_summary(path):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, "rb") as source:
        info = os.fstat(source.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_size > MAX_RECORD_BYTES:
            raise ValueError("expected a bounded regular file")
        value = source.read(MAX_RECORD_BYTES + 1)
    if len(value) > MAX_RECORD_BYTES:
        raise ValueError("summary exceeds byte limit")
    return checked(parse(value), path.name)


class HttpElastic:
    def __init__(self, endpoint, *, allow_loopback_http=False, api_key=None, timeout=5):
        url = urlsplit(endpoint)
        if (not url.hostname or url.username is not None or url.password is not None
                or url.path not in ("", "/") or url.query or url.fragment):
            raise ValueError("expected an Elasticsearch origin, without credentials or path")
        insecure = (allow_loopback_http and url.scheme == "http"
                    and url.hostname in ("127.0.0.1", "::1"))
        if url.scheme != "https" and not insecure:
            raise ValueError("HTTPS required except for explicit literal loopback testing")
        if api_key and insecure:
            raise ValueError("API keys require HTTPS")
        self.origin = f"{url.scheme}://{url.netloc.lower()}"
        self.port, self.host = url.port, url.hostname
        self.factory = http.client.HTTPConnection if insecure else http.client.HTTPSConnection
        self.api_key, self.timeout = api_key, timeout

    def request(self, method, path, body=None):
        conn = self.factory(self.host, port=self.port, timeout=self.timeout)
        headers = {"Accept": "application/json"}
        if self.api_key:
            headers["Authorization"] = "ApiKey " + self.api_key
        if body is not None:
            headers["Content-Type"] = "application/x-ndjson"
        try:
            conn.request(method, path, body=body, headers=headers)
            with conn.getresponse() as response:
                if response.status != 200:
                    raise Retry(f"Elasticsearch HTTP {response.status}; no acknowledgment")
                data = response.read(MAX_RESPONSE_BYTES + 1)
                if len(data) > MAX_RESPONSE_BYTES:
                    raise Retry("Elasticsearch response exceeds byte limit")
            result = parse(data)
            if not isinstance(result, dict):
                raise Retry("Elasticsearch response is not an object")
            return result
        except (http.client.HTTPException, OSError, ValueError, RecursionError) as err:
            raise Retry("Unable to verify Elasticsearch response") from err
        finally:
            conn.close()


class Collector:
    def __init__(self, spool, index, elastic, *, max_items=100, max_scan=1000):
        if not INDEX.fullmatch(index) or not 1 <= max_items <= 250 or not max_items <= max_scan <= 10_000:
            raise ValueError("invalid concrete index or batch/scan limit")
        self.spool, self.index, self.elastic = spool, index, elastic
        self.max_items, self.max_scan = max_items, max_scan

    def run(self):
        info = self.spool.lstat()
        if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o077:
            raise ValueError("spool must be a private directory owned by the collector user")
        fd = os.open(self.spool / ".collect.lock",
                     os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
        with os.fdopen(fd, "rb+") as lock:
            try:
                fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError as err:
                raise Retry("spool already has an active collector") from err
            return self._run()

    def _check_route(self):
        binding = self.spool / ".collect-target"
        route = self.elastic.request("GET", f"/{self.index}/_settings/index.uuid?flat_settings=true")
        if set(route) != {self.index}:
            raise ValueError("target must resolve to its own concrete index, not an alias or data stream")
        resolved = route[self.index]
        settings = resolved.get("settings") if isinstance(resolved, dict) else None
        index_uuid = settings.get("index.uuid") if isinstance(settings, dict) else None
        if not isinstance(index_uuid, str) or not re.fullmatch(r"[a-zA-Z0-9_-]{1,64}", index_uuid):
            raise ValueError("unable to establish concrete index generation")
        target = encode({"origin": self.elastic.origin, "index": self.index, "index_uuid": index_uuid})
        with_binding = binding.exists()
        if with_binding:
            fd = os.open(binding, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
            with os.fdopen(fd, "rb") as source:
                if not stat.S_ISREG(os.fstat(source.fileno()).st_mode) or source.read(1024) != target:
                    raise ValueError("spool is bound to a different Elasticsearch target")
        if not with_binding:
            temp = self.spool / ".collect-target.tmp"
            temp.unlink(missing_ok=True)
            fd = os.open(temp, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
            with os.fdopen(fd, "wb") as output:
                output.write(target)
                output.flush()
                os.fsync(output.fileno())
            os.replace(temp, binding)
            fd = os.open(self.spool, os.O_RDONLY | os.O_DIRECTORY)
            try:
                os.fsync(fd)
            finally:
                os.close(fd)

    def _quarantine(self, path):
        quarantine = self.spool / ".rejected"
        quarantine.mkdir(mode=0o700, exist_ok=True)
        info = quarantine.lstat()
        if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o077:
            raise ValueError("quarantine must be a private directory owned by the collector user")
        destination = quarantine / path.name
        if os.path.lexists(destination):
            return False
        path.rename(destination)  # Same filesystem, only collector writes quarantine.
        return True

    def _run(self):
        result = dict(scanned=0, offered=0, created=0, duplicate=0,
                      invalid=0, rejected=0, deferred=0, conflict=0)
        pending = []
        with os.scandir(self.spool) as entries:
            for entry in entries:
                result["scanned"] += 1
                if FILENAME.fullmatch(entry.name):
                    path = Path(entry.path)
                    try:
                        value = read_summary(path)
                    except FileNotFoundError:
                        continue
                    except (UnicodeError, ValueError, RecursionError):
                        if self._quarantine(path):
                            result["invalid"] += 1
                        else:
                            result["deferred"] += 1
                    except OSError as err:
                        if err.errno == errno.ELOOP and self._quarantine(path):
                            result["invalid"] += 1
                        else:
                            result["deferred"] += 1
                    else:
                        pending.append((path, value, identity(value)))
                if result["scanned"] == self.max_scan or len(pending) == self.max_items:
                    break
        if not pending:
            return result
        self._check_route()
        body = b"".join(encode({"create": {"_id": key}}) + b"\n" + encode(value) + b"\n"
                        for _, value, key in pending)
        result["offered"] = len(pending)
        response = self.elastic.request("POST", f"/{self.index}/_bulk", body)
        items = self._validate_items(response, pending)
        for (path, value, key), item in zip(pending, items):
            status = item["status"]
            if status == 201:
                path.unlink()
                result["created"] += 1
            elif status == 409:
                try:
                    stored = self.elastic.request("GET", f"/{self.index}/_doc/{key}")
                except Deadline:
                    raise
                except Retry:
                    stored = {}
                if (stored.get("_index") == self.index and stored.get("_id") == key
                        and stored.get("found") is True):
                    if stored.get("_source") == value and _same_json_types(stored["_source"], value):
                        path.unlink()
                        result["duplicate"] += 1
                        continue
                    result["conflict"] += 1
                result["deferred"] += 1
            elif status == 400 and self._quarantine(path):
                result["rejected"] += 1
            else:
                # Keep ambiguous, rate-limited or misconfigured writes on local storage.
                result["deferred"] += 1
        return result

    def _validate_items(self, response, pending):
        items = response.get("items")
        if not isinstance(items, list) or len(items) != len(pending):
            raise Retry("Bulk response item count mismatch")
        verified = []
        for raw, (_, _, key) in zip(items, pending):
            item = raw.get("create") if isinstance(raw, dict) else None
            if (not isinstance(item, dict) or item.get("_index") != self.index
                    or item.get("_id") != key or type(item.get("status")) is not int
                    or not 100 <= item["status"] <= 599):
                raise Retry("Bulk response action/index/ID mismatch")
            verified.append(item)
        if response.get("errors") is not any(i["status"] != 201 for i in verified):
            raise Retry("Bulk response error flag mismatch")
        return verified


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--spool", required=True, type=Path)
    p.add_argument("--index", required=True)
    p.add_argument("--elastic", required=True)
    p.add_argument("--allow-loopback-http", action="store_true")
    p.add_argument("--max-items", type=int, default=100)
    p.add_argument("--max-scan", type=int, default=1000)
    p.add_argument("--deadline", type=int, default=30, help="Total pass deadline in seconds (1–120)")
    args = p.parse_args()
    if not 1 <= args.deadline <= 120:
        p.error("deadline must be between 1 and 120 seconds")

    def expire(_signum, _frame):
        raise Deadline("Pass deadline reached; unresolved summaries remain on disk")

    signal.signal(signal.SIGALRM, expire)
    signal.alarm(args.deadline)
    try:
        elastic = HttpElastic(args.elastic, allow_loopback_http=args.allow_loopback_http,
                              api_key=os.environ.get("QAAS_ELASTIC_API_KEY"))
        result = Collector(args.spool, args.index, elastic,
                           max_items=args.max_items, max_scan=args.max_scan).run()
    except Retry as err:
        print(str(err), file=sys.stderr)
        return 2
    except (OSError, ValueError):
        print("Collector configuration or spool I/O failed; see the collector guide", file=sys.stderr)
        return 1
    finally:
        signal.alarm(0)
    print(json.dumps(result, sort_keys=True))
    return 3 if result["invalid"] or result["rejected"] or result["conflict"] else (2 if result["deferred"] else 0)


if __name__ == "__main__":
    raise SystemExit(main())
