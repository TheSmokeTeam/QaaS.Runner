# QaaS.Runner observability verification

Pull request 78 was measured locally on Microsoft Windows 10 Home, 11th Gen Intel(R) Core(TM) i5-1135G7 @ 2.40GHz, 8 logical processors, 7.70 GiB RAM, .NET SDK 10.0.203, Node v24.16.0 and Python Python 3.14.5. The isolated services ran Elasticsearch 7.17.29 and Grafana 11.6.0. Time windows in the evidence are UTC.

The QaaS platform developer plugin was enabled at version 0.5.0. Its workflow skill matched the upstream revision `7aa8ad9e1de3f14d016c97929ccbc2f7fb7536c8`. The implementation used the reviewed plan and independent code and dashboard checks. Docker memory limits were 640 MiB for Elasticsearch and 240 MiB for Grafana.

| Build | Commit |
|---|---|
| Merge base | `750dda829d0aa11616056382023cdf0b7f50ddac` |
| Previous PR | `e1b71431fed45d89311f84944fd93e8f6dfd8d3f` |
| Reviewed source and dashboard | `5a7fea36db809f3c0ee0910a8ef146a4c5a7f7d8` |
| Paired product source | `707d875037018b080a38074208e4e1c562e7685a` |

The final CLI loaded `QaaS.Runner.dll` with SHA-256 `c3eb48295e4ab5678daab75313b49aebabbc4ef730ead6b4e22e049b3ba8dfe1`. Dashboard/config-only commits after the relevant runtime build are identified in the functional and performance bundles.

## Runtime and deployment

Each built run, act or assert plan gets a fresh attempt ID. The existing Debug logger emits a start and a finish with ownership, mode, version, configured features and case/batch membership. The finish adds the outcome, elapsed time, test plans and completed assertions. An interrupted parallel assertion batch can leave its count unknown. The application’s per-assertion diagnostics retain their existing severities and messages.

Configure the existing Elasticsearch sink or pass `--send-logs --elastic-uri <url>`. Supply trusted `MetaData.Team`, `MetaData.System` and string `MetaData.ExtraLabels.Project`. Install the native ingest processor and mappings on the current and future raw log indices, then start the product transform. Grafana uses index `qaasusage-runner-v1`, time field `event.Time` and data source UID `qaas-runner-usage-v1`. Launch the process with `QAAS_USAGE_ENABLED=false` to disable new usage records and keep ordinary logging. Restart a running service to apply the environment change. The transform can be stopped independently.

## What was verified

Across the two-CLI workload, Runner recorded 6 starts and 6 finishes, 4 full test plans, 3 assertions and 1 unsuccessful finish (16.7%). The payer fixture failed an actual assertion against Mocker. Case expansion produced two measured batch cases. Four starts had no trusted project.

The final Runner Debug stream matched its merge base in content and order for all 85 ordinary lines on the passing fixture and 103 on the failing fixture, after normalizing clocks and generated attachment identifiers. Exit codes remained 0 and 1. Mocker retained its route-miss, fallback, exception and shutdown diagnostics in the real Kestrel workload.

All 1,110 Runner and 485 Mocker Release tests passed on the final runtime. One Runner discovery test missed its 120 ms response window on the first run under load. It passed in the complete isolated rerun. Grafana imported both dashboard JSON files and loaded both again from disk. The initial independent oracle compared 504 panel and scenario results to hand counts and event-level calculations. It distinguished 20 genuine CLI records, 11 exact redeliveries of genuine records, and 43 labeled fault records. The latter exercised partial counts, late/absent phases, unknown ownership, weighted counters and conflicting replays. The repeat against both final runtime binaries passed 504 additional panel comparisons across all seven dashboard filters, separately and in combination. Its receipt, filtered frame checks and binary hashes are in the archive.

## Performance

| Comparison | Workload | Elasticsearch | Pairs | Reference median | Candidate median | Paired overhead, 95% interval |
|---|---|---|---:|---:|---:|---|
| Merge base → candidate | runner-high-assertion | healthy | 4 | 4.143 s | 3.855 s | -20.8% [-69.1%, +103.0%] |
| Merge base → candidate | runner-high-assertion | slow | 4 | 9.785 s | 10.338 s | +5.8% [-9.6%, +23.7%] |
| Merge base → candidate | runner-high-assertion | unreachable | 4 | 19.917 s | 20.057 s | +0.3% [-6.4%, +7.5%] |
| Usage off → on | runner-high-assertion | healthy | 3 | 6.137 s | 5.719 s | +19.8% [-42.4%, +149.4%] |
| Usage off → on | runner-high-assertion | slow | 3 | 11.785 s | 10.476 s | -10.1% [-36.7%, +27.6%] |
| Usage off → on | runner-high-assertion | unreachable | 3 | 21.282 s | 24.168 s | +0.5% [-34.3%, +53.7%] |
| Previous PR → candidate | runner-high-assertion | healthy | 4 | 5.437 s | 4.317 s | -32.0% [-76.5%, +96.9%] |

Positive overhead means a slower operation or fewer requests per second. Timing intervals use the paired log ratio and apply separately to each comparison. Multiple comparisons were not adjusted. HTTP windows share warmed server processes. The complete observations include counts, byte sizes, CPU, working set, actual CLI exit and client-side tail latency. Healthy requests used local Elasticsearch. The slow relay delayed accepted bulk writes by one second, and the unavailable endpoint refused connections on loopback. WAN packet loss, DNS/TLS timeouts and multi-node production cluster load were outside these trials. This shared 8 GB machine cannot establish a universal 2% bound.

### Performance gate

The 2% ceiling remains unverified. Across 24 real Runner-to-Mocker launches, the 128-assertion workload produced 3,072 assertions and the expected exits. Healthy Elasticsearch gave a paired wall-time change of −0.98% with a 95% interval of −9.80% to +8.71%. Delaying accepted bulk writes by one second gave −0.23% with an interval of −23.06% to +29.36%. The standalone healthy high-assertion workload preserved all 258 ordinary diagnostic records in both arms and added two lifecycle records with usage enabled.

A two-command batch containing five executions was 21.2% slower in nine whole-process pairs (95% interval +13.6% to +29.5%). In eight follow-up pairs with randomized order and a fixed configuration, about 413 ms of the mean 492 ms difference preceded the first execution. From first execution through exit, the mean difference was +80 ms (interval −165 to +325 ms). Both the initial regression and the uncertainty about its cause remain relevant to sign-off.

The first lifecycle pair in separate cold processes measured 0.35 ms with usage disabled and 1.93 ms enabled. Warm healthy start/finish calls measured 2.43 µs disabled and 40.13 µs enabled, with 7,848 allocated bytes per measured pair. These narrower call measurements exclude downstream sink completion.

Six EventPipe captures found the main startup stack in runtime type discovery. In the three enabled processes, stacks containing the usage producer accounted for approximately 2 ms of sampled managed thread time across five executions. This sampling does not establish that the producer caused the observed whole-process regression. The archive includes phase measurements, sample limits and reproduction commands.


## Operational limits

Counts cover observed telemetry from runs that entered execution. The transform projects an explicit field list. Ordinary logs retain their configured enrichment, and the dashboard reads organizational labels and generated attempt IDs. A long-running service or an abruptly stopped process may have no finish. Retain the raw indices through the maximum run and replay horizon. A late update after source expiry can lose the older phase during recomputation. The native transform has no built-in destination expiry in this configuration. Active and returning owners are approximate counts of known labels. Identical labels across teams collapse in the dimension-wide count. Use team filtering when the labels are only unique within a team. No eligible-team denominator is available. A full Run is the unit of test execution. A hook may perform additional internal checks. Broken assertions, failed assertions, cancellation and Runner errors have distinct terminal outcomes. Repository defaults and embedded custom loggers can change whether Debug records reach Elasticsearch.

The development binaries reported version `0.0.0`. The version filter was tested with this observed value. Release binaries report the assembly version supplied by their build.

## KPI reference

# Dashboard KPI definitions

The dashboard time zone is UTC. All panels honor team, project, system, environment, mode, version and feature filters. Start and finish timestamps are tested independently against the selected interval. An attempt crossing a boundary can contribute to one side. Canonical execution rows carry all observed phase times so Grafana’s outer time filter can retain either side.

## Measurement and reconciliation

The source is the existing application log stream indexed in Elasticsearch. The native ingest pipeline timestamps arrival. Continuous transforms reconcile attempts and HTTP batches, including duplicate and late deliveries. Refresh is scheduled every 30 seconds with a 10-second ingestion delay. Allow the transform and index refresh to finish before comparing.

Every Runner plan invocation and Mocker hosting attempt has its own `AttemptId`. One execution row pairs the earliest equivalent start and earliest equivalent finish. A repeated invocation has a distinct ID. Mocker batches use `ServerId` plus `BatchSequence`; a restart creates a fresh server ID. Each nonempty batch carries deltas. Idle intervals produce no sample. Repeated copies with different values, or disagreement between common immutable start/finish dimensions, mark a key conflicted. Operational panels exclude it. The conflict panel counts the affected keys.

The bins display the Framework environment and configured capabilities for that execution mode. A single attempt can appear in several capability bins. Components may be configured when a failure prevents later plan stages from running. Configuration supplies team/system labels and the explicit project label. Missing attribution is indexed under `(unattributed)`; literal trusted `Unknown` is a distinct label. Missing environment/version becomes `Unknown`, and an empty capability set becomes `none`. A filter narrows the population before aggregations, including owner cardinalities and rate denominators.

## Panels shared by both products

| Panel | Calculation within the selection |
|---|---|
| Runs started | Distinct canonical execution rows whose start timestamp is in range. |
| Runs completed | Distinct canonical execution rows whose terminal timestamp is in range. Includes successful and unsuccessful observed terminals. |
| Unsuccessful finishes | Sum of numeric `Failed` flags divided by observed terminal attempts. Cancellations count as unsuccessful finishes. |
| P95 run duration | Elasticsearch percentile estimate over measured terminal durations in milliseconds. Both products measure their lifecycle interval using a monotonic clock. |
| Starts by environment | Count by start-time histogram and Framework CI/local label. |
| Finished outcomes | Count by terminal-time histogram and outcome. Runner distinguishes assertion failure, broken assertion, cancellation and Runner error. Mocker distinguishes its hosting outcomes. |
| No observed finish after 15 min | Starts in range at least 15 minutes before the query end with no finish by that end. An active long-lived service also qualifies once it crosses the threshold. |
| Finish without observed start | In-range terminal records whose canonical row lacks a retained start. A retained start before the range still counts as present. |
| Completed runs / s | Number of in-range finishes divided by the full chosen interval in seconds, including idle time. |
| Unknown attribution | Starts with at least one missing trusted ownership dimension. |
| Conflicting replays | Number of canonical keys with incompatible copies or immutable phase dimensions; each conflicted key appears once. |
| Active teams, projects, systems | Per dimension, estimated distinct known labels among observed starts. These are counts of usage. An eligible-owner denominator would be required for an adoption percentage. |
| Returning teams, projects, systems | Estimated distinct known labels that occur in both equal-duration halves. The midpoint belongs to the second half. The query bounds the overlap estimate between zero and the smaller group. A half without observable owners makes the comparison unavailable. |
| Usage by team, project and system | Starts grouped by up to five teams, five projects per team and five systems per project, ordered by volume. |
| Usage by version and mode | Starts grouped by up to eight versions and eight modes per version. |
| Feature use | Starts per configured capability and time bucket. Adding the series can count one execution multiple times. |
| Outcomes by team and system | Up to eight teams and eight systems per team, with finishes, unsuccessful count and unsuccessful fraction. |
| Recent unsuccessful executions | Up to 25 unsuccessful terminals with finish time, trusted ownership, execution mode, outcome, monotonic duration and the opaque attempt ID. |

The owner popularity metrics use Elasticsearch cardinality with precision threshold 4,000. Low-cardinality fixtures have exact hand-counted comparisons here. Partial or missing records affect observed counts. An absent group can render a dash, and an actual measured zero remains a zero.

## Runner panels

| Panel | Unit and timing |
|---|---|
| Test plans executed | Sum of full `Run` attempts entered, attributed to finish time. An expanded case is one plan. Split `Act` and `Assert` phases contribute zero plans. |
| Assertions executed | Sum of completed assertion results for finishes with an exact count. Reusing an execution object counts only the new results for the current attempt. |
| Assertions / s | Sum of observed completed assertions divided by the range duration in seconds. |
| Unmeasured assertion counts | Number of finishes missing an exact count. An interrupted parallel batch can leave results unpublished. |
| Mode, case and batch | Finishes, full plans and observed completed assertions grouped by `Run`, `Act` or `Assert` and standalone, named case, batch command or batch case. The assertion total is absent when every finish in the group lacks a count. |

Runner emits lifecycle records only after a measured execution enters its plan. Parsing or configuration failures before that point, help and template generation do not produce usage pairs. Assertion hooks may perform their own checks internally. The counted unit is the completed Runner assertion result.

## Mocker panels

| Panel | Unit and timing |
|---|---|
| Completed requests | Sum of `Requests` deltas across nonconflicting HTTP batches published within the range. |
| Handler error rate | Sum of `HandlerErrors` divided by summed requests. This includes recovered processing/response failures. |
| Route miss rate | Sum of `Misses` divided by summed requests. Includes route/method misses handled by the not-found path. |
| HTTP 5xx rate | Sum of actual 500–599 responses divided by summed requests, including intentionally mocked 5xx responses. |
| HTTP requests / s | Requests in each flush-time histogram bucket divided by that bucket’s duration in seconds. |
| HTTP outcomes over time | Separate sums per flush-time bucket of at least 10 seconds for handler errors, route misses and 5xx. The same response can contribute to more than one series. |
| Busiest HTTP server instances | Up to 20 distinct server lifetimes ranked by summed requests, with port and each failure counter. |

Ratios use summed numerators and denominators across batches; a small flush carries proportionally less weight. Requests that never reach a recorded completion or a published batch are unobserved. The fault inputs exercise unequal batch sizes and a conflicting batch; the genuine workload independently witnesses the two cross-project responses.


## Full benchmark tables

# Runner release performance verification

PR source head 5a7fea36db809f3c0ee0910a8ef146a4c5a7f7d8. Measured runtime SHA-256 c3eb48295e4ab5678daab75313b49aebabbc4ef730ead6b4e22e049b3ba8dfe1. Merge base 750dda829d0aa11616056382023cdf0b7f50ddac. Documentation, Elasticsearch and Grafana settings were committed after the identical runtime was built.

258 observations in 129 matched pairs across 36 workload and comparison settings. Absolute values show within-arm medians. Percentage change compares aligned alternating arms. Ratios use positive values for slower operation. Confidence limits are paired two-sided 95% intervals on log ratios. Absolute differences are available for metrics with a zero baseline.

The candidate-usage comparison toggles QAAS_USAGE_ENABLED on the same assembly with identical sink settings. Base-head uses the merge base. Previous-head uses the prior published PR revision. Same-controls launches exactly the same binary and configuration in both arms. Flush-zero compares an explicit zero wait with the default, with usage enabled on both arms. Explicit flush-choice compares the default with the separately configured 2,000 ms shutdown allowance.


## Comparison with the 2% target

These counts use the same final binaries with global usage off and on under otherwise matched logging. The throughput row counts one main statistic per workload and condition. Positive throughput overhead means more elapsed time for an equal number of requests. HTTP batches within a long-running host share that host, so its peak-working-set observations are not independent machine samples.

| Measure | Conditions | Entire 95% interval ≤ +2% | Entire interval > +2% | Crosses target |
|---|---:|---:|---:|---:|
| Process CPU seconds per CLI | 31 | 2 | 1 | 28 |
| CLI or request throughput | 31 | 4 | 1 | 26 |
| Peak process working set | 31 | 16 | 0 | 15 |

Conditions with the full interval above +2% follow. The raw observations retain each original run, including any follow-up pairs.

- runner-batch, healthy CLI wall time: +21.25% (+13.56% to +29.45%, 9 pairs).

## Workload timing and process resources

| Comparison | Workload | Elastic | n | Off / base | On / current | Overhead and 95% interval | CPU off/on (s) | Peak working set off/on (MiB) |
|---|---|---|---:|---:|---:|---|---:|---:|
| base-head | runner-high-assertion | healthy | 4 | 4.143 s | 3.855 s | -20.78% (-69.09% to +103.04%) | 4.48/4.51 | 149.28/151.06 |
| base-head | runner-high-assertion | healthy | 4 | 31.131 assertions/s | 33.233 assertions/s | -20.78% (-69.09% to +103.04%) | 4.48/4.51 | 149.28/151.06 |
| base-head | runner-high-assertion | slow | 4 | 9.785 s | 10.338 s | +5.78% (-9.56% to +23.71%) | 6.17/6.54 | 153.09/152.35 |
| base-head | runner-high-assertion | slow | 4 | 13.175 assertions/s | 12.381 assertions/s | +5.78% (-9.56% to +23.71%) | 6.17/6.54 | 153.09/152.35 |
| base-head | runner-high-assertion | unreachable | 4 | 19.917 s | 20.057 s | +0.30% (-6.43% to +7.51%) | 5.73/5.64 | 147.37/147.60 |
| base-head | runner-high-assertion | unreachable | 4 | 6.427 assertions/s | 6.384 assertions/s | +0.30% (-6.43% to +7.51%) | 5.73/5.64 | 147.37/147.60 |
| candidate-usage | runner-24-sessions | healthy | 9 | 2.251 s | 2.225 s | -4.31% (-24.04% to +20.55%) | 2.09/2.11 | 140.44/140.45 |
| candidate-usage | runner-act | healthy | 3 | 2.614 s | 1.626 s | -40.47% (-68.91% to +13.97%) | 2.39/1.62 | 142.12/141.97 |
| candidate-usage | runner-assert | healthy | 3 | 2.025 s | 1.635 s | -19.32% (-24.92% to -13.30%) | 2.00/1.66 | 140.88/140.04 |
| candidate-usage | runner-assertion-fail | disabled | 3 | 1.937 s | 1.361 s | -14.53% (-61.15% to +88.02%) | 1.97/1.44 | 123.40/123.29 |
| candidate-usage | runner-assertion-fail | healthy | 3 | 3.609 s | 2.614 s | -10.26% (-55.69% to +81.73%) | 3.80/2.45 | 144.98/144.30 |
| candidate-usage | runner-assertion-fail | slow | 3 | 5.378 s | 4.079 s | -3.82% (-47.36% to +75.73%) | 5.25/3.48 | 148.04/147.30 |
| candidate-usage | runner-assertion-fail | unreachable | 3 | 10.397 s | 10.135 s | -1.57% (-16.10% to +15.49%) | 4.69/5.19 | 141.49/140.17 |
| candidate-usage | runner-batch | healthy | 9 | 2.037 s | 2.406 s | +21.25% (+13.56% to +29.45%) | 2.00/2.31 | 142.96/142.99 |
| candidate-usage | runner-cases | healthy | 3 | 2.246 s | 2.142 s | -6.82% (-56.53% to +99.74%) | 2.16/1.70 | 140.82/140.88 |
| candidate-usage | runner-high-assertion | disabled | 3 | 4.100 s | 3.251 s | -15.25% (-29.84% to +2.38%) | 3.81/3.58 | 131.45/130.18 |
| candidate-usage | runner-high-assertion | disabled | 3 | 31.222 assertions/s | 39.371 assertions/s | -15.25% (-29.84% to +2.38%) | 3.81/3.58 | 131.45/130.18 |
| candidate-usage | runner-high-assertion | healthy | 3 | 6.137 s | 5.719 s | +19.80% (-42.45% to +149.37%) | 5.44/6.44 | 147.39/147.12 |
| candidate-usage | runner-high-assertion | healthy | 3 | 20.858 assertions/s | 22.381 assertions/s | +19.80% (-42.45% to +149.37%) | 5.44/6.44 | 147.39/147.12 |
| candidate-usage | runner-high-assertion | slow | 3 | 11.785 s | 10.476 s | -10.13% (-36.72% to +27.63%) | 9.45/7.20 | 153.08/151.94 |
| candidate-usage | runner-high-assertion | slow | 3 | 10.862 assertions/s | 12.219 assertions/s | -10.13% (-36.72% to +27.63%) | 9.45/7.20 | 153.08/151.94 |
| candidate-usage | runner-high-assertion | unreachable | 3 | 21.282 s | 24.168 s | +0.51% (-34.27% to +53.68%) | 7.80/10.47 | 147.64/151.57 |
| candidate-usage | runner-high-assertion | unreachable | 3 | 6.014 assertions/s | 5.296 assertions/s | +0.51% (-34.27% to +53.68%) | 7.80/10.47 | 147.64/151.57 |
| candidate-usage | runner-invalid | disabled | 3 | 0.521 s | 0.431 s | -26.16% (-66.72% to +63.83%) | 0.50/0.38 | 35.48/34.27 |
| candidate-usage | runner-invalid | healthy | 3 | 1.187 s | 1.046 s | -15.95% (-77.15% to +209.19%) | 0.98/1.02 | 57.47/59.66 |
| candidate-usage | runner-invalid | slow | 3 | 1.165 s | 0.828 s | -37.84% (-61.65% to +0.75%) | 1.11/0.77 | 58.82/59.03 |
| candidate-usage | runner-invalid | unreachable | 3 | 4.941 s | 6.053 s | +11.70% (-18.89% to +53.82%) | 0.83/1.66 | 47.54/46.23 |
| candidate-usage | runner-short | disabled | 4 | 2.169 s | 1.686 s | -18.55% (-61.32% to +71.52%) | 1.86/1.55 | 117.61/117.65 |
| candidate-usage | runner-short | healthy | 4 | 2.078 s | 2.132 s | +4.01% (-40.01% to +80.33%) | 2.02/2.04 | 139.57/139.01 |
| candidate-usage | runner-short | slow | 4 | 3.566 s | 3.330 s | -7.17% (-36.92% to +36.63%) | 2.88/2.55 | 142.78/142.11 |
| candidate-usage | runner-short | unreachable | 4 | 8.253 s | 7.910 s | -4.04% (-10.60% to +3.00%) | 2.70/2.57 | 134.40/134.65 |
| candidate-usage | runner-storage-fail | healthy | 3 | 1.850 s | 2.113 s | +15.85% (-20.40% to +68.60%) | 1.77/2.05 | 143.23/142.16 |
| candidate-usage | runner-template | healthy | 3 | 2.127 s | 1.592 s | -21.95% (-37.28% to -2.86%) | 2.12/1.55 | 139.69/138.21 |
| candidate-usage | runner-to-mocker-assertions | disabled | 3 | 2.951 s | 3.355 s | +17.02% (-17.57% to +66.13%) | 3.03/3.70 | 133.86/135.45 |
| candidate-usage | runner-to-mocker-assertions | disabled | 3 | 43.377 assertions/s | 38.152 assertions/s | +17.02% (-17.57% to +66.13%) | 3.03/3.70 | 133.86/135.45 |
| candidate-usage | runner-to-mocker-assertions | healthy | 3 | 4.052 s | 4.130 s | -0.98% (-9.80% to +8.71%) | 4.88/4.67 | 150.17/150.20 |
| candidate-usage | runner-to-mocker-assertions | healthy | 3 | 31.586 assertions/s | 30.996 assertions/s | -0.98% (-9.80% to +8.71%) | 4.88/4.67 | 150.17/150.20 |
| candidate-usage | runner-to-mocker-assertions | slow | 3 | 10.419 s | 10.262 s | -0.23% (-23.06% to +29.36%) | 6.42/6.33 | 152.54/152.72 |
| candidate-usage | runner-to-mocker-assertions | slow | 3 | 12.286 assertions/s | 12.473 assertions/s | -0.23% (-23.06% to +29.36%) | 6.42/6.33 | 152.54/152.72 |
| candidate-usage | runner-to-mocker-assertions | unreachable | 3 | 20.423 s | 19.975 s | -1.64% (-2.85% to -0.42%) | 6.47/6.02 | 149.03/148.86 |
| candidate-usage | runner-to-mocker-assertions | unreachable | 3 | 6.267 assertions/s | 6.408 assertions/s | -1.64% (-2.85% to -0.42%) | 6.47/6.02 | 149.03/148.86 |
| candidate-usage | runner-unowned | disabled | 3 | 1.206 s | 1.441 s | +31.56% (-39.03% to +183.86%) | 1.19/1.44 | 108.20/108.23 |
| candidate-usage | runner-unowned | healthy | 3 | 1.900 s | 1.722 s | -13.74% (-28.10% to +3.50%) | 1.84/1.70 | 128.12/128.16 |
| candidate-usage | runner-unowned | slow | 3 | 2.040 s | 2.324 s | +37.92% (-43.83% to +238.63%) | 2.03/2.14 | 128.19/128.14 |
| candidate-usage | runner-unowned | unreachable | 3 | 6.406 s | 6.898 s | +11.06% (-24.84% to +64.10%) | 2.09/2.70 | 118.69/119.13 |
| previous-head | runner-high-assertion | healthy | 4 | 5.437 s | 4.317 s | -32.04% (-76.54% to +96.85%) | 6.07/5.12 | 149.25/149.55 |
| previous-head | runner-high-assertion | healthy | 4 | 23.564 assertions/s | 29.663 assertions/s | -32.04% (-76.54% to +96.85%) | 6.07/5.12 | 149.25/149.55 |
| same-controls | runner-high-assertion | healthy | 4 | 4.409 s | 3.952 s | -11.27% (-37.19% to +25.35%) | 5.06/4.30 | 149.58/151.10 |
| same-controls | runner-high-assertion | healthy | 4 | 29.056 assertions/s | 32.415 assertions/s | -11.27% (-37.19% to +25.35%) | 5.06/4.30 | 149.58/151.10 |

## Elasticsearch ingestion

CLI rows below aggregate the actual acknowledgements across independent processes. A delayed bulk request can finish after its process exits. Each relay stays alive outside the timed window to record that acknowledgement. Closed/disabled destinations accept zero documents. The usage-byte column counts compact uncompressed serialized usage event bodies. Total wire bytes also include ordinary diagnostic documents and bulk metadata.

| Comparison | Workload | Elastic | Arm | Runs | Accepted usage | Accepted diagnostics | Usage document bytes | All bulk wire bytes |
|---|---|---|---|---:|---:|---:|---:|---:|
| base-head | runner-high-assertion | healthy | off | 4 | 0 | 1032 | 0 | 28536 |
| base-head | runner-high-assertion | healthy | on | 4 | 8 | 1032 | 9786 | 32131 |
| base-head | runner-high-assertion | slow | off | 4 | 0 | 1032 | 0 | 28606 |
| base-head | runner-high-assertion | slow | on | 4 | 8 | 1032 | 9784 | 31713 |
| base-head | runner-high-assertion | unreachable | off | 4 | 0 | 0 | 0 | 0 |
| base-head | runner-high-assertion | unreachable | on | 4 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-24-sessions | healthy | off | 9 | 0 | 432 | 0 | 9491 |
| candidate-usage | runner-24-sessions | healthy | on | 9 | 18 | 432 | 20772 | 13711 |
| candidate-usage | runner-act | healthy | off | 3 | 0 | 6 | 0 | 1215 |
| candidate-usage | runner-act | healthy | on | 3 | 6 | 6 | 7062 | 2594 |
| candidate-usage | runner-assert | healthy | off | 3 | 0 | 3 | 0 | 927 |
| candidate-usage | runner-assert | healthy | on | 3 | 6 | 3 | 6946 | 2309 |
| candidate-usage | runner-assertion-fail | disabled | off | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-assertion-fail | disabled | on | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-assertion-fail | healthy | off | 3 | 0 | 12 | 0 | 1846 |
| candidate-usage | runner-assertion-fail | healthy | on | 3 | 6 | 12 | 7320 | 3233 |
| candidate-usage | runner-assertion-fail | slow | off | 3 | 0 | 12 | 0 | 1843 |
| candidate-usage | runner-assertion-fail | slow | on | 3 | 6 | 12 | 7326 | 3246 |
| candidate-usage | runner-assertion-fail | unreachable | off | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-assertion-fail | unreachable | on | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-batch | healthy | off | 9 | 0 | 90 | 0 | 4718 |
| candidate-usage | runner-batch | healthy | on | 9 | 90 | 90 | 104382 | 12966 |
| candidate-usage | runner-cases | healthy | off | 3 | 0 | 24 | 0 | 1480 |
| candidate-usage | runner-cases | healthy | on | 3 | 24 | 24 | 27670 | 3598 |
| candidate-usage | runner-high-assertion | disabled | off | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-high-assertion | disabled | on | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-high-assertion | healthy | off | 3 | 0 | 774 | 0 | 21433 |
| candidate-usage | runner-high-assertion | healthy | on | 3 | 6 | 774 | 7342 | 23794 |
| candidate-usage | runner-high-assertion | slow | off | 3 | 0 | 774 | 0 | 21464 |
| candidate-usage | runner-high-assertion | slow | on | 3 | 6 | 774 | 7340 | 23760 |
| candidate-usage | runner-high-assertion | unreachable | off | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-high-assertion | unreachable | on | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-invalid | disabled | off | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-invalid | disabled | on | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-invalid | healthy | off | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-invalid | healthy | on | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-invalid | slow | off | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-invalid | slow | on | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-invalid | unreachable | off | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-invalid | unreachable | on | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-short | disabled | off | 4 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-short | disabled | on | 4 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-short | healthy | off | 4 | 0 | 8 | 0 | 1616 |
| candidate-usage | runner-short | healthy | on | 4 | 8 | 8 | 9230 | 3399 |
| candidate-usage | runner-short | slow | off | 4 | 0 | 8 | 0 | 1616 |
| candidate-usage | runner-short | slow | on | 4 | 8 | 8 | 9232 | 3398 |
| candidate-usage | runner-short | unreachable | off | 4 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-short | unreachable | on | 4 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-storage-fail | healthy | off | 3 | 0 | 6 | 0 | 1211 |
| candidate-usage | runner-storage-fail | healthy | on | 3 | 6 | 6 | 7092 | 2596 |
| candidate-usage | runner-template | healthy | off | 3 | 0 | 3 | 0 | 928 |
| candidate-usage | runner-template | healthy | on | 3 | 0 | 3 | 0 | 930 |
| candidate-usage | runner-to-mocker-assertions | disabled | off | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-to-mocker-assertions | disabled | on | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-to-mocker-assertions | healthy | off | 3 | 0 | 771 | 0 | 21331 |
| candidate-usage | runner-to-mocker-assertions | healthy | on | 3 | 6 | 771 | 7314 | 23609 |
| candidate-usage | runner-to-mocker-assertions | slow | off | 3 | 0 | 771 | 0 | 21242 |
| candidate-usage | runner-to-mocker-assertions | slow | on | 3 | 6 | 771 | 7316 | 23586 |
| candidate-usage | runner-to-mocker-assertions | unreachable | off | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-to-mocker-assertions | unreachable | on | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-unowned | disabled | off | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-unowned | disabled | on | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-unowned | healthy | off | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-unowned | healthy | on | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-unowned | slow | off | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-unowned | slow | on | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-unowned | unreachable | off | 3 | 0 | 0 | 0 | 0 |
| candidate-usage | runner-unowned | unreachable | on | 3 | 0 | 0 | 0 | 0 |
| previous-head | runner-high-assertion | healthy | off | 4 | 8 | 1032 | 4064 | 29568 |
| previous-head | runner-high-assertion | healthy | on | 4 | 8 | 1032 | 9788 | 31900 |
| same-controls | runner-high-assertion | healthy | off | 4 | 0 | 1032 | 0 | 28658 |
| same-controls | runner-high-assertion | healthy | on | 4 | 0 | 1032 | 0 | 29215 |

## Five-execution batch attribution

A separate timestamp probe launched the identical fixture path and owner in 8 new matched processes per arm, with balanced randomized order. It timestamped existing standard output at high resolution, read child CPU time, and recorded native bulk arrivals and acknowledgements. The first execution marker precedes the first global usage-switch read. Mean paired differences below are additive.

| Phase | Off median (ms) | On median (ms) | Mean added (ms) and 95% interval |
|---|---:|---:|---:|
| Before first execution | 2176 | 2420 | +413 (-216 to +1043) |
| Five actual executions | 120 | 134 | -48 (-219 to +124) |
| After executions to final CLI log | 140 | 191 | +120 (-10 to +250) |
| Final CLI log to process exit | 32 | 35 | +7 (-5 to +20) |
| All time after first execution | 285 | 359 | +80 (-165 to +325) |

[Raw marker, CPU and transport observations](batch-phase.jsonl)

## Compiled execution logging cost

In one warmed CLI process, the real compiled execution-start and finish methods ran against the configured logger. The time for an empty delegate is subtracted within each batch. Each on/off mode has 16 paired batches after warmup.

| Logging config | Added microseconds per execution | Added bytes per execution |
|---|---:|---:|
| [off-disabled](runner-increment-off-disabled.json) | 0.014 | 0 |
| [off-healthy](runner-increment-off-healthy.json) | 2.427 | 32 |
| [off-unreachable](runner-increment-off-unreachable.json) | 2.869 | 32 |
| [on-disabled](runner-increment-on-disabled.json) | 0.035 | 0 |
| [on-healthy](runner-increment-on-healthy.json) | 40.131 | 7848 |
| [on-unreachable](runner-increment-on-unreachable.json) | 19.119 | 7848 |

The first real lifecycle invocation on a fresh healthy process had a median of 0.35 ms when usage was disabled and 1.93 ms enabled (4 independent processes per arm). These timings include first-call initialization; logger shutdown is outside this profile.

## Independent follow-up pairs

The healthy workloads below triggered follow-up runs when the initial interval lay above +2%. Positive indicates higher elapsed time per operation.

| Workload and metric | Initial pairs | Follow-up pairs | Combined estimate |
|---|---:|---:|---:|
| runner-24-sessions, wall_s | +25.48% (+9.52% to +43.76%, n=3) | -16.43% (-37.56% to +11.84%, n=6) | -4.31% (-24.04% to +20.55%, n=9) |
| runner-batch, wall_s | +14.82% (+3.34% to +27.58%, n=3) | +24.59% (+13.28% to +37.04%, n=6) | +21.25% (+13.56% to +29.45%, n=9) |

8 observations from a superseded candidate are available separately. They are excluded from this release table. [Intermediate observations](intermediate-observations.jsonl)

[Statistics](statistics.csv) · [Paired raw observations](observations.jsonl) · [Method](METHOD.md) · [Environment](machine.json)

## Reproduction and downloads

The [complete verification archive](verification.zip) includes two independent reproduction kits. The functional kit includes the two CLI configurations, an isolated Docker Compose environment and a saved panel oracle. The benchmark kit contains the workload sources, asset and build preparation commands and process/HTTP probes. Each includes a runbook. Raw timing observations and confidence intervals accompany the benchmark report.
