import json
import tempfile
import unittest
from pathlib import Path

from collect import Collector, Deadline, ProducerGate, Retry, identity

ID = "a" * 32
IDX = "qaas-attempts-v1-000001"


def record(attempt=ID, **fields):
    value = dict(schema_version=1, attempt_id=attempt, project_key="b" * 32,
                 operation="run", outcome="pass", completed_at="2026-01-01T00:00:00.1234567Z",
                 duration_ms=0, exit_code=0)
    return value | fields


class Elastic:
    origin = "https://elastic.invalid"

    def __init__(self, statuses=(201,), stored=None, malformed=False, alias=False, uuid="generation_1"):
        self.statuses, self.stored, self.malformed = statuses, stored, malformed
        self.alias = alias
        self.uuid = uuid
        self.calls = []
        self.docs = []

    def request(self, method, path, body=None):
        self.calls.append((method, path))
        if "/_settings/" in path:
            index = path.split("/")[1]
            return {("real-concrete" if self.alias else index): {"settings": {"index.uuid": self.uuid}}}
        if method == "GET":
            return {"_index": IDX, "_id": path.rsplit("/", 1)[1],
                    "found": True, "_source": self.stored}
        data = [json.loads(line) for line in body.splitlines()]
        self.docs.extend(data[1::2])
        actions = data[::2]
        return {"errors": any(s != 201 for s in self.statuses),
                "items": [{"create": {"_index": IDX, "_id": "wrong" if self.malformed else action["create"]["_id"],
                                     "status": status,
                                     "error": {"type": "document_parsing_exception"} if status == 400 else None}}
                          for action, status in zip(actions, self.statuses)]}


class CollectorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.spool = Path(self.temp.name)

    def write(self, value, name=None):
        p = self.spool / (name or value["attempt_id"] + ".json")
        p.write_text(json.dumps(value))
        return p

    def collect(self, fake, **kwargs):
        return Collector(self.spool, IDX, fake, **kwargs).run()

    def test_mixed_item_outcomes_preserve_retry_and_quarantine_invalid(self):
        files = [self.write(record(str(i)*32)) for i in range(1, 5)]
        fake = Elastic((201, 400, 429, 409), stored=record("4"*32))
        result = self.collect(fake)
        by_id = {d["attempt_id"]: f for d in fake.docs for f in files if d["attempt_id"] == f.stem}
        for d, retained in zip(fake.docs, [False, False, True, dups_maybe(fake)]):
            self.assertEqual(by_id[d["attempt_id"]].exists(), retained)
        self.assertEqual(result["created"], 1)
        self.assertEqual(result["deferred"], 1 + dups_maybe(fake))
        self.assertEqual(result["duplicate"], 1 - dups_maybe(fake))
        self.assertEqual(result["rejected"], 1)

    def test_duplicate_exact_is_acknowledged_but_conflict_is_not(self):
        p = self.write(record())
        self.assertEqual(self.collect(Elastic((409,), stored=record()))["duplicate"], 1)
        self.assertFalse(p.exists())
        p = self.write(record(duration_ms=1))
        self.assertEqual(self.collect(Elastic((409,), stored=record()))["deferred"], 1)
        self.assertTrue(p.exists())

    def test_bad_batch_response_acks_nothing(self):
        p = self.write(record())
        with self.assertRaises(Retry):
            self.collect(Elastic(malformed=True))
        self.assertTrue(p.exists())

    def test_routing_cannot_change_in_existing_spool(self):
        self.write(record())
        self.collect(Elastic())
        self.write(record())
        with self.assertRaises(ValueError):
            Collector(self.spool, "qaas-attempts-v1-000002", Elastic()).run()

    def test_recreated_concrete_name_not_a_replay_target(self):
        self.write(record())
        self.collect(Elastic())
        self.write(record())
        with self.assertRaises(ValueError):
            self.collect(Elastic(uuid="generation_2"))

    def test_network_deadline_not_swallowed_by_duplicate_resolution(self):
        p = self.write(record())
        class SlowReply(Elastic):
            def request(self, method, path, body=None):
                if "/_doc/" in path:
                    raise Deadline("deadline")
                return super().request(method, path, body)
        with self.assertRaises(Deadline):
            self.collect(SlowReply((409,)))
        self.assertTrue(p.exists())

    def test_expected_canary_missing_but_ordinary_idle_is_healthy(self):
        other = self.write(record("d" * 32))
        fake = Elastic()
        with self.assertRaisesRegex(ProducerGate, "canary is missing"):
            Collector(self.spool, IDX, fake).run(expected_attempt=ID, expected_project_key="b" * 32)
        self.assertTrue(other.exists())
        self.assertEqual(fake.calls, [])
        other.unlink()
        self.assertEqual(self.collect(fake)["offered"], 0)
        self.assertEqual(fake.calls, [])

    def test_expected_canary_rejects_wrong_project_and_accepts_only_known_file(self):
        known = self.write(record())
        other = self.write(record("d" * 32))
        fake = Elastic()
        collector = Collector(self.spool, IDX, fake)
        with self.assertRaisesRegex(ProducerGate, "different project key"):
            collector.run(expected_attempt=ID, expected_project_key="c" * 32)
        self.assertTrue(known.exists())
        self.assertEqual(fake.calls, [])
        result = collector.run(expected_attempt=ID, expected_project_key="b" * 32)
        self.assertEqual((result["created"], result["scanned"]), (1, 1))
        self.assertEqual(fake.docs, [record()])
        self.assertTrue(other.exists())
        self.assertFalse(known.exists())

    def test_expected_canary_preserves_invalid_file(self):
        path = self.write(record(schema_version=2))
        fake = Elastic()
        with self.assertRaisesRegex(ProducerGate, "valid schema-v1 summary"):
            Collector(self.spool, IDX, fake).run(expected_attempt=ID, expected_project_key="b" * 32)
        self.assertTrue(path.exists())
        self.assertEqual(fake.calls, [])

    def test_alias_is_rejected_before_send(self):
        p = self.write(record())
        fake = Elastic(alias=True)
        with self.assertRaises(ValueError):
            self.collect(fake)
        self.assertTrue(p.exists())
        self.assertEqual(len(fake.calls), 1)

    def test_outside_link_and_invalid_unknown_field_not_sent(self):
        bad = self.write(record(secret="should not leave"))
        (self.spool / ("c"*32+".json")).symlink_to("/dev/zero")
        (self.spool / "partial.tmp").write_text("incomplete")
        fake = Elastic()
        result = self.collect(fake)
        self.assertEqual(result["invalid"], 2)
        self.assertEqual(fake.calls, [])
        self.assertFalse(bad.exists())
        self.assertTrue((self.spool / "partial.tmp").exists())

    def test_bounds_keep_pending_record_and_id_not_bare_attempt(self):
        a, b = self.write(record()), self.write(record("d"*32))
        fake = Elastic()
        result = self.collect(fake, max_items=1)
        self.assertEqual(result["created"], 1)
        self.assertNotEqual(a.exists(), b.exists())
        self.assertNotEqual(identity(record()), ID)
        self.assertNotEqual(identity(record()), identity(record(project_key="c"*32)))

def dups_maybe(fake):
    return int(fake.docs[-1]["attempt_id"] != "4"*32)


if __name__ == "__main__":
    unittest.main()
