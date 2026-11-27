# QaaS observability: Linux follow-up, 2026-11-27

**Keep both implementation PRs in draft.** The new local tests and data checks passed, but they do not establish cross-platform performance readiness. In particular, short Mocker launches exceed the performance budget.

| Component | New draft commit | Published operator assets |
|---|---|---|
| Runner | `4f10856f681690cf993f2ef085024d34faeb31b6` | `320b05b7e7ce080bf1f75a3b4e49706edd5ae473` |
| Mocker | `e66f79bf77bd7d0967b12748a2162a8d3c612a72` | `265859576ea77a85874533ece393a1386d83df8d` |

## Correctness and deployment

- Runner now uses the same precedence and validation as Mocker for the three allowed organizational labels. Its new regression failed on the prior head: a control-bearing label was treated as known. It passed with the fix; a blank configured value falls back to a root value, an invalid nonblank value does not, and valid code-first metadata wins.
- Mocker HTTP summaries now permit at most one in-flight logger write per server. The new regression held the first write and proved that a later flush did not start another or consume its counts. It passed after the fix. This is not a bound on how long an arbitrary synchronous provider may hold graceful exit. No per-request log, new service, or transport was added.
- At the final source, 1,123 Runner and 489 Mocker Release tests passed on Linux. A pre-existing template test used a Windows-only real working directory; the fixture now uses a platform-native path with the mock filesystem. It does not modify production code.
- Actual Runner-to-Mocker HTTP behavior and exit codes matched with telemetry off and on. On recorded five requests, one routing miss, one handler error and two HTTP 5xx responses, including an intentional 503. Elasticsearch acknowledged exactly the expected lifecycle and HTTP records.
- Clean Elasticsearch 7.17.29 and Grafana 11.6.0 imported the exact, commit-pinned Console scripts and JSON. A pre-existing final-pipeline marker survived; the current and next template-created daily indices accepted ordinary logs. Starts and finishes across the synthetic midnight reconciled once per product even with a replay. Grafana passed 168 checks via its actual query API across unfiltered, team-filtered and empty views. The earlier PR screenshots remain the visual reference; no new browser screenshot is claimed.

## Performance and delivery

Sixteen independently launched, order-alternated Linux pairs per product; same binary within each pair, native Elasticsearch sink via a local relay, and no profiler attached. Exit time is monotonic through actual process exit; memory is the OS peak resident set from wait4. Values are paired geometric percent change (two-sided 95% Student intervals). Positive numbers mean more cost. Runner performs one act and four runs per launch; Mocker performs a short redirected-input launch. The upper confidence bound must stay at or below +2% for the relevant performance check.

| Probe | Runner | Mocker |
|---|---:|---:|
| Complete lifecycle pairs, enabled | 80/80 | 16/16 |
| Exit time | −3.42% [−7.93%, +1.31%] | +9.05% [+3.92%, +14.43%] |
| Peak resident memory | −0.08% [−0.29%, +0.13%] | +8.98% [+8.65%, +9.31%] |
| Ordinary indexed diagnostic records, off → on | 160 → 160 | 0 → 0 |

The short Runner Linux result clears 2% for this fixture only. It does not invalidate the earlier Windows inconclusive result. Mocker startup errors after execution start (existing invalid certificate contents, not a missing-file validation failure) preserved their nonzero exit; both baseline and candidate retained all eight start/finish pairs each across healthy and delayed relay runs. The earlier Windows 13/16 short Mocker result is not resolved by this Linux sample.

The remote [Runner](https://github.com/TheSmokeTeam/QaaS.Runner/actions/runs/46006370813) and [Mocker](https://github.com/TheSmokeTeam/QaaS.Mocker/actions/runs/46006303712) exact-head Windows CI runs passed and skipped package publication. The runtime binaries were compiled from the local candidate logic; later Runner edits before commit were formatting/README only. Local binary checksums are included where measured. No production deployment, TLS/WAN check, or merge/release was performed.

### Sustained HTTP

4 independent alternating host pairs, 5,000 warmup requests per host, then 32 seconds of measured successful HTTP load and 16 clients. Recorded counters matched warmup and measured completions: 2,342,570 requests represented by 16 HTTP summary documents, with no per-request usage event. The enabled runs crossed at least three periodic publish intervals. Ordinary application/Kestrel diagnostics were left unchanged. Reported overhead estimates (estimate, 95% lower and upper, percent):

- `rps [1.845, -4.926, 9.098]`
- `p95_ms [4.103, -5.472, 14.648]`

### Drain-budget sensitivity (four launches per budget)

This separate small sample measures delivery, not performance sign-off. Readback from the raw index is authoritative. One completed bulk became visible after the immediate relay snapshot; the archive retains both observations. A faster arm that omits events is not a candidate.

- `0 0 / 4 median exit ms 1583`
- `32 0 / 4 median exit ms 1481`
- `75 4 / 4 median exit ms 1449`
- `125 4 / 4 median exit ms 1487`

## Next gate

Keep both release gates open. On Windows repeat the Runner paired short benchmark through actual process exit. For Mocker, identify what portion of short-process overhead is required for delivery and explicitly reconcile launches against native receipts; do not reduce the drain budget just to meet a timing target. Verify graceful exit with a deliberately delayed provider separately from the measured native path. Size Elastic rollover and expiry from indexed bytes and checkpoint lag, not compressed wire bytes.
