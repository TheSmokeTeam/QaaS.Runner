# Collect QaaS Runner attempt summaries into Elasticsearch

This optional Linux tool runs **outside** the Runner and reads only its opted-in, terminal summary spool. It performs no log scraping, does not instrument the test process, and has no effect on runner outcomes. It does not implement scheduling, expected-run SLOs, Grafana provisioning, monitoring, or production deployment.

## Inputs and setup

**Producer prerequisite:** Use a pinned Runner build that includes the [optional schema-v1 attempt summary producer](../../README.md#optional-local-attempt-summary). Older builds without this producer ignore the three environment variables below. Verify the actual build and launch path with the setup canary before scheduling normal collection.

Use a private local-filesystem spool, owned by the same restricted service account as the Runner and collector, with mode 0700. Set the Runner's QAAS_ATTEMPT_SUMMARY_DIRECTORY to that directory and provide its valid QAAS_ATTEMPT_ID and QAAS_PROJECT_KEY identities. The Runner must atomically publish each immutable UTF-8 summary as a complete file named with its attempt ID and a .json suffix. The collector ignores temporary files and accepts at most 1 KiB per record. This is a separate process from the Runner; run it from an independent supervisor, not as a post-step that prolongs the test process.

An administrator provisions a concrete index named qaas-attempts-v1-<generation>, using the supplied [strict mapping](index-mapping.json). Preserve the default stored source, since accepted retries are verified against it. The writer must be allowed to read settings for that index (view_index_metadata), create documents (create_doc) and read what it previously wrote (read). Scope permissions only to the assigned index. Do not grant index creation, alias management, delete or index administration to the collector. Supply an encoded API key in QAAS_ELASTIC_API_KEY through a secret manager; never pass it as a URL component or include it in logs. Use HTTPS with a trusted CA. The literal loopback HTTP switch is for disposable unauthenticated tests only; it refuses an API key.

```sh
python3 tools/observability/collect.py \
  --spool "$QAAS_ATTEMPT_SUMMARY_DIRECTORY" \
  --elastic https://elastic.example.invalid \
  --index qaas-attempts-v1-000001
```

### Required setup canary

1. Before starting the normal collector, record an independently assigned, unique canary attempt ID and expected project key. Use a private disposable spool and test index. Start the **deployed Runner build**, under the intended service account, with those keys and a known-good local test via a top-level run, act or assert command. Require the fixture's expected exit code. Help, version, template and nested execute commands are not producer canaries. Do not hand-write the summary.
2. Run one targeted collection pass, substituting the same IDs and the isolated test destination. Keep the normal supervisor paused so it cannot collect the canary first.

```sh
python3 tools/observability/collect.py \
  --spool "$QAAS_ATTEMPT_SUMMARY_DIRECTORY" \
  --elastic "$QAAS_CANARY_ELASTIC" --index "$QAAS_CANARY_INDEX" \
  --expect-attempt "$QAAS_CANARY_ATTEMPT_ID" \
  --expect-project-key "$QAAS_CANARY_PROJECT_KEY"
```

3. Require exit 0 and one created or exactly verified duplicate record before enabling normal collection. The gate inspects only this expected file; absent, incompatible and mismatched records fail the setup. If the gate fails, verify the producer revision, invocation kind, opt-in values and spool permissions. Use a new canary attempt ID for a fresh setup check.

Normal scheduled collection omits both expected-canary options. A quiet spool is valid during ordinary operation; use independent expectations to detect missing scheduled work.

Schedule repeated passes with bounded supervisor backoff and monitor disk free space, backlog age, exit statuses and results. A pass scans at most 1,000 entries, batches at most 100 real records and has a 30-second total deadline by default; documented CLI flags adjust these limits. One collector per spool is enforced with an OS file lock. This is a local spool, not a distributed lock or queue. The operator must provide a disk quota and retention; large/blocked spools can defer later entries. A persistent conflict or permission failure needs operator action, not a tight retry loop. Do not schedule it in the Runner process.

## Delivery and stable identity

The stable ID is SHA-256 over a versioned namespace and the project/attempt identities. The collector checks the concrete target's name and generation UUID, then binds the spool to that origin/index/UUID before sending. Aliases and data streams are deliberately not supported. This prevents an existing spool's retry from silently going to a new rollover generation, including when a concrete name is deleted and recreated. Provision a **new spool and target** for a new generation; keep the old index open through its retry window and continue draining the old spool. Route each attempt back to its original spool. Do not delete/reset the binding, recreate a target while collectors are running, reuse an attempt identity across generations, or republish an old record from a different spool. This is stable routing within a correctly managed spool; it is not global exactly-once. Restrict who can provision/change targets. Set refresh and retention according to the approved policy.

The collector uses Bulk API creates and validates all returned item IDs, actions and concrete index names before deleting any local input. A successful create removes that file. A conflict checks the exact stored source via real-time read: an identical retry removes the file, a divergent or unverifiable conflict remains for intervention. A malformed response or ambiguous HTTP outcome preserves unresolved inputs. An item-level 400 or invalid local summary is moved to a private .rejected queue, never sent as raw logs or printed. Keep this directory restricted and expire it under an approved policy; it can contain invalid inputs. Temp files are ignored. Other failures remain pending. Rejected and conflict items signal exit 3; temporary failures exit 2; spool configuration/I/O errors exit 1. Exit 0 means the particular pass raised no observed error, not that the whole spool is drained.

## Dashboard consumption

Read summaries from accepted indexed documents, not the spool or the diagnostic log index. Count attempts and bounded terminal outcomes; use completed_at for filtering, project_key/operation for authorized cohorts, and duration_ms for timing. Select the concrete generations approved for that reporting window; maintain generation boundaries so a cross-generation query cannot double count. A dashboard needs a separate read-only identity and must keep protected identifiers out of Prometheus labels. Do not use these documents alone to count expected runs, scheduler deadlines, Mocker metrics, or fleet health. Accepted counts become query-visible on the Elastic refresh schedule. If the data path is unavailable, report it as unavailable, not zero.

## Verify locally

Run the standard-library unit tests from this directory:

```sh
python3 -m unittest -v test_collect.py
```

The optional smoke_elastic.py checks a disposable Elasticsearch 8.15.5 node, with cluster name qaas-collector-synthetic and HTTP bound only to 127.0.0.1:19256. It provisions a unique test index, exercises the actual CLI, verifies exact search counts and checks duplicate/divergent outcomes and route changes. Do not use a shared or production node. Stop and discard the test node afterward. The optional --accepted-jsonl writes a new file from exact accepted synthetic search hits, not from the pending spool.
