"""CLI and metric-semantics tests using the independently prepared KPI fixture."""
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parent

def metric(body, family, window="7d", team="all", environment="all"):
    labels = f'{{window="{window}",team="{team}",environment="{environment}"}}'
    match = re.search(r"^qaas_runner_kpi_" + re.escape(family + labels) + r" (\S+)$",body,re.M)
    return float(match[1]) if match else None

class Integration(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.dir.cleanup)
        self.base = Path(self.dir.name)
        self.accepted = self.base/"accepted.jsonl"
        self.accepted.write_bytes((ROOT/"fixtures/accepted.jsonl").read_bytes())
        self.roster = self.base/"roster.json"
        self.roster.write_bytes((ROOT/"fixtures/roster.json").read_bytes())
        self.ledger = self.base/"ledger.json"
        self.ledger.write_bytes((ROOT/"fixtures/ledger.json").read_bytes())
        self.output = self.base/"current.prom"

    def cli(self, ledger=True):
        cmd=[sys.executable,str(ROOT/"readmodel.py"),"--accepted",str(self.accepted),
             "--roster",str(self.roster),"--as-of","2026-11-22T16:00:00Z","--output",str(self.output)]
        if ledger:
            cmd+=["--ledger",str(self.ledger)]
        return subprocess.run(cmd,capture_output=True,text=True)

    def ok(self, **kwargs):
        response=self.cli(**kwargs)
        self.assertEqual(response.returncode,0,response.stderr)
        return self.output.read_text()

    def events(self):
        return [json.loads(s) for s in self.accepted.read_text().splitlines()]

    def set_events(self, rows):
        self.accepted.write_text("".join(json.dumps(r)+"\n" for r in rows))

    def test_prior_oracle_denominators_and_window_boundaries(self):
        s=self.ok()
        for window,eligible,active,attempts,covered,expected,good in (
            ("7d",5,3,7,6,7,4),("30d",5,4,10,9,10,7)):
            self.assertAlmostEqual(metric(s,"adoption_ratio",window),active/eligible)
            for metric_name,value in (("eligible_projects",eligible),("active_projects",active),
                                      ("attempts",attempts),("covered",covered),("expected",expected),
                                      ("platform_good",good),("unexpected",1),("missing",1)):
                self.assertEqual(metric(s,metric_name,window),value,metric_name)
        self.assertEqual(metric(s,"duration_p50_seconds"),3)
        self.assertEqual(metric(s,"duration_p95_seconds"),11)
        self.assertIsNone(metric(s,"adoption_ratio",team="payments",environment="staging"))

    def test_replay_reordering_and_validation_without_leaking_ids(self):
        original=self.ok()
        events=list(reversed(self.events()))
        self.set_events(events+events[:5])
        self.assertEqual(self.ok(),original)
        events.append(dict(events[0],duration_ms=0 if events[0]["duration_ms"] else 1))
        self.set_events(events)
        response=self.cli()
        self.assertEqual(response.returncode,2)
        self.assertIn("conflicting terminal",response.stderr)
        self.assertNotIn(events[0]["attempt_id"],response.stderr)
        self.assertEqual(self.output.read_text(),original)

    def test_completed_unknown_and_outside_roster_stay_distinct(self):
        events=self.events()
        known=next(r for r in events if r["completed_at"]=="2026-11-21T16:15:00.000Z")
        known["outcome"]="completed"
        ghost=dict(known,attempt_id="1"*32,project_key="2"*32,outcome="unknown")
        self.set_events(events+[ghost])
        s=self.ok()
        self.assertEqual(metric(s,"completed"),1)
        self.assertEqual(metric(s,"pass"),2)
        self.assertEqual(metric(s,"unrostered_attempts"),1)
        self.assertIsNone(metric(s,"unrostered_attempts",team="payments",environment="ci"))
        self.assertNotIn(ghost["project_key"],s)
        self.assertNotIn(ghost["attempt_id"],s)
        self.assertEqual(metric(s,"operation_run_attempts")+metric(s,"operation_act_attempts")+metric(s,"operation_assert_attempts"),metric(s,"attempts"))
        self.assertAlmostEqual(metric(s,"adoption_ratio"),3/5)

    def test_empty_and_absent_independent_denominators(self):
        before=self.ok(ledger=False)
        self.assertEqual(metric(before,"ledger_available"),0)
        self.assertIsNone(metric(before,"expected"))
        self.assertIsNone(metric(before,"unexpected"))
        self.assertIsNone(metric(before,"platform_good_ratio"))
        self.assertAlmostEqual(metric(before,"adoption_ratio"),3/5)
        self.set_events([])
        s=self.ok()
        self.assertEqual(metric(s,"adoption_ratio"),0)
        self.assertEqual(metric(s,"coverage_ratio"),0)
        self.assertEqual(metric(s,"duration_samples"),0)
        self.assertIsNone(metric(s,"duration_p95_seconds"))
        self.assertIsNone(metric(s,"functional_failure_ratio"))

    def test_pending_maturity_and_independent_snapshot(self):
        ledger=json.loads(self.ledger.read_text())
        item=next(r for r in ledger["attempts"] if r["due_at"]=="2026-11-21T15:00:00Z")
        item["mature_at"]="2026-11-22T16:00:00Z"
        self.ledger.write_text(json.dumps(ledger))
        s=self.ok()
        self.assertEqual(metric(s,"expected"),6)
        self.assertEqual(metric(s,"covered"),5)
        self.assertEqual(metric(s,"pending_maturity_observed"),1)
        self.assertEqual(metric(s,"unexpected"),1)

    def test_no_debug_content_shape_time_scope_or_size(self):
        base=self.ok()
        rows=self.events()
        for key,value in (("completed_at","2026-11-21T12:34:56+00:00"),("attempt_id","0"*32),
                          ("duration_ms",True),("schema_version",True),("secret","privacy sentinel"),
                          ("outcome","error\nleak"),("project_key","x"*9000)):
            with self.subTest(key=key):
                self.set_events([dict(rows[0],**{key:value})])
                r=self.cli()
                self.assertEqual(r.returncode,2)
                self.assertNotIn(str(value),r.stderr)
                self.assertEqual(self.output.read_text(),base)

    def test_precise_fractional_edges_and_ledger_project_mismatch(self):
        accepted=self.events()
        project=accepted[0]["project_key"]
        rows=[]
        for i,stamp in enumerate(("2026-11-15T15:59:59.9999999Z","2026-11-15T16:00:00.0000000Z",
                                  "2026-11-22T15:59:59.9999999Z","2026-11-22T16:00:00.0000000Z"),10):
            rows.append(dict(accepted[0],attempt_id=f"{i:032x}",completed_at=stamp))
        self.set_events(rows)
        self.assertEqual(metric(self.ok(),"attempts"),2)
        self.assertEqual(metric(self.ok(),"attempts","30d"),3)
        change=accepted[0]["attempt_id"]
        rows.append(dict(accepted[0],project_key="b"*32))
        self.set_events(rows)
        r=self.cli()
        self.assertEqual(r.returncode,2)
        self.assertIn("terminal and expected ledger project conflict",r.stderr)
        self.assertNotIn(change,r.stderr)

    def test_independent_empty_roster_is_not_adoption_zero(self):
        roster=json.loads(self.roster.read_text())
        roster["projects"]=[]
        self.roster.write_text(json.dumps(roster))
        s=self.ok(ledger=False)
        self.assertEqual(metric(s,"eligible_projects"),0)
        self.assertIsNone(metric(s,"adoption_ratio"))
        self.assertGreater(metric(s,"unrostered_attempts"),0)
        roster["teams"].append("a\nlabel-injection")
        self.roster.write_text(json.dumps(roster))
        r=self.cli(ledger=False)
        self.assertEqual(r.returncode,2)
        self.assertNotIn("label-injection",r.stderr)


if __name__=="__main__":
    unittest.main()
