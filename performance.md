# QaaS.Runner CLI performance follow-up

Runner preserves discovered configurator types per assembly while creating new configurator instances for each command. It narrows reflection to assemblies that can reach the configurator contract. Unresolved dependencies, partial loads, custom load contexts, ambiguous identities and self-contained deployment layouts retain conservative resolution. Scoped lifecycle logs preserve the typed dashboard fields and render a compact event name and attempt ID. A batch with zero data sources now retains the existing generator debug record and skips creating a provider that has nothing to resolve. Configured generators take the original path.

For the real one-act/four-run batch, a one-off instrumented build made six calls to Assembly.GetTypes across the two child commands. The second command reused all six successful entries. A separate EventPipe check of the final build found zero samples in the empty generator assembly scan, compared with 26.7–139.5 ms in six earlier traces. It recorded all five ordinary generator diagnostics and Elasticsearch acknowledged 10 ordinary records, five starts and five finishes. Recorded scan and sampling results measure those particular call paths. Whole-process estimates and their uncertainty appear below.

## Source and environment

| Component | Git SHA |
|---|---|
| Merge base | `750dda829d0aa11616056382023cdf0b7f50ddac` |
| Previously reviewed PR | `5a7fea36db809f3c0ee0910a8ef146a4c5a7f7d8` |
| Measured candidate | `320b05b7e7ce080bf1f75a3b4e49706edd5ae473` |

Windows 10 Home, Intel Core i5-1135G7, four physical cores, eight logical processors and 7.7 GiB usable memory. .NET SDK 10.0.203. CPython 3.14.5 sampled each Windows process through launch and exit. The Docker Elasticsearch 7.17.29 container had a 640 MiB limit. The existing ChatGPT desktop session remained open. Console output, CPU, memory, sink endpoint, execution inputs and deadlines were matched within a block. The raw receipts include each binary digest and measured outcomes. [Original verification and dashboards](https://github.com/TheSmokeTeam/QaaS.Runner/blob/ecffc1152c481c1e458605a1544b59c572ed2530/verification.md)

## CLI wall time

Each row reports a paired geometric change and two-sided 95% t interval on log ratios. The columns report marginal medians. A positive percentage means more elapsed time for the work. The upper interval must be at most +2% to confirm the requested overhead ceiling. Every launched block is present in the raw evidence. The printers and network receipt checks ran outside timed intervals. This is a process-level study on one physical machine.

| Scenario | Comparison | Pairs | Reference median | Candidate median | Paired change (95% interval) |
|---|---|---:|---:|---:|---:|
| runner-aa | head-off → control-off | 8 | 2.831 s | 2.972 s | +8.35% [-8.11%, +27.75%] |
| runner-before-warm | prior-off → prior-on | 12 | 2.833 s | 2.737 s | -10.99% [-31.39%, +15.46%] |
| runner-before | prior-off → prior-on | 6 | 2.581 s | 2.448 s | +25.34% [-49.64%, +211.99%] |
| runner-disabled | base-off → head-on | 4 | 2.155 s | 2.209 s | -8.71% [-54.71%, +84.00%] |
| runner-disabled | head-off → head-on | 4 | 2.311 s | 2.209 s | -3.74% [-29.90%, +32.18%] |
| runner-empty-sources | base-off → head-on | 20 | 2.811 s | 2.934 s | +7.78% [-7.39%, +25.44%] |
| runner-empty-sources | head-on → next-on | 20 | 2.934 s | 2.723 s | -9.59% [-18.99%, +0.91%] |
| runner-empty-sources | next-off → next-on | 20 | 2.715 s | 2.723 s | -5.28% [-21.07%, +13.68%] |
| runner-empty-sources | base-off → next-on | 20 | 2.811 s | 2.723 s | -2.55% [-13.66%, +9.98%] |
| runner-final-disabled | base-off → head-on | 3 | 2.208 s | 2.270 s | +1.75% [-31.54%, +51.23%] |
| runner-final-disabled | head-off → head-on | 3 | 2.127 s | 2.270 s | +8.30% [-33.69%, +76.88%] |
| runner-final-slow | base-off → head-on | 3 | 3.778 s | 3.783 s | -0.70% [-9.13%, +8.50%] |
| runner-final-slow | head-off → head-on | 3 | 3.537 s | 3.783 s | +5.72% [-4.19%, +16.66%] |
| runner-final-unreachable | base-off → head-on | 3 | 8.321 s | 9.316 s | +9.78% [-7.37%, +30.10%] |
| runner-final-unreachable | head-off → head-on | 3 | 9.425 s | 9.316 s | -3.28% [-24.63%, +24.12%] |
| runner-incremental | base-off → head-on | 60 | 2.756 s | 2.738 s | +2.35% [-3.84%, +8.93%] |
| runner-incremental | head-off → head-on | 60 | 2.722 s | 2.738 s | +2.72% [-2.89%, +8.66%] |
| runner-linux | base-off → head-on | 15 | 2.824 s | 3.250 s | +5.38% [-18.93%, +36.97%] |
| runner-linux | head-off → head-on | 15 | 2.768 s | 3.250 s | +8.48% [-13.23%, +35.63%] |
| runner-primary-final | base-off → head-on | 20 | 1.756 s | 1.722 s | -5.43% [-11.11%, +0.62%] |
| runner-primary-final | prior-on → head-on | 20 | 1.701 s | 1.722 s | +1.59% [-2.46%, +5.82%] |
| runner-primary-final | head-off → head-on | 20 | 1.809 s | 1.722 s | -4.51% [-8.88%, +0.07%] |
| runner-primary | base-off → head-on | 20 | 2.400 s | 2.277 s | -10.81% [-23.96%, +4.60%] |
| runner-primary | prior-on → head-on | 20 | 2.241 s | 2.277 s | +7.51% [-8.78%, +26.71%] |
| runner-primary | head-off → head-on | 20 | 2.145 s | 2.277 s | +6.93% [-6.93%, +22.85%] |
| runner-slow | base-off → head-on | 4 | 3.731 s | 3.740 s | -0.32% [-11.25%, +11.96%] |
| runner-slow | head-off → head-on | 4 | 3.885 s | 3.740 s | -2.51% [-10.00%, +5.60%] |
| runner-unreachable | base-off → head-on | 4 | 8.949 s | 9.662 s | +5.94% [-27.14%, +54.04%] |
| runner-unreachable | head-off → head-on | 4 | 10.700 s | 9.662 s | -6.39% [-33.56%, +31.89%] |

## Peak working set

Windows GetProcessMemoryInfo reports the process high-water mark. The harness reads it at intervals of up to 25 ms while the child is alive. Private bytes are also recorded in the CSV. A peak working set is resident memory and includes managed, native, and mapped pages.

| Scenario | Comparison | Pairs | Reference median | Candidate median | Paired change (95% interval) |
|---|---|---:|---:|---:|---:|
| runner-aa | head-off → control-off | 8 | 144.18 MiB | 143.98 MiB | +0.06% [-0.41%, +0.54%] |
| runner-before-warm | prior-off → prior-on | 12 | 143.11 MiB | 143.26 MiB | -0.06% [-0.65%, +0.54%] |
| runner-before | prior-off → prior-on | 6 | 143.26 MiB | 143.38 MiB | +0.80% [-1.07%, +2.71%] |
| runner-disabled | base-off → head-on | 4 | 122.84 MiB | 124.65 MiB | +1.30% [+0.71%, +1.90%] |
| runner-disabled | head-off → head-on | 4 | 124.69 MiB | 124.65 MiB | -0.07% [-0.38%, +0.24%] |
| runner-empty-sources | base-off → head-on | 20 | 143.15 MiB | 143.56 MiB | +0.19% [+0.01%, +0.37%] |
| runner-empty-sources | head-on → next-on | 20 | 143.56 MiB | 144.04 MiB | +0.37% [+0.18%, +0.57%] |
| runner-empty-sources | next-off → next-on | 20 | 143.99 MiB | 144.04 MiB | +0.22% [+0.07%, +0.36%] |
| runner-empty-sources | base-off → next-on | 20 | 143.15 MiB | 144.04 MiB | +0.56% [+0.31%, +0.81%] |
| runner-final-disabled | base-off → head-on | 3 | 122.68 MiB | 124.29 MiB | +1.29% [+0.86%, +1.73%] |
| runner-final-disabled | head-off → head-on | 3 | 124.50 MiB | 124.29 MiB | -0.08% [-0.50%, +0.35%] |
| runner-final-slow | base-off → head-on | 3 | 146.15 MiB | 147.72 MiB | +0.72% [-2.82%, +4.40%] |
| runner-final-slow | head-off → head-on | 3 | 147.29 MiB | 147.72 MiB | +0.40% [-1.24%, +2.06%] |
| runner-final-unreachable | base-off → head-on | 3 | 140.72 MiB | 142.30 MiB | +1.45% [+0.74%, +2.15%] |
| runner-final-unreachable | head-off → head-on | 3 | 142.34 MiB | 142.30 MiB | +0.19% [-0.58%, +0.97%] |
| runner-incremental | base-off → head-on | 60 | 143.18 MiB | 143.47 MiB | +0.38% [+0.21%, +0.55%] |
| runner-incremental | head-off → head-on | 60 | 143.70 MiB | 143.47 MiB | +0.03% [-0.14%, +0.20%] |
| runner-primary-final | base-off → head-on | 20 | 142.98 MiB | 143.41 MiB | +0.38% [+0.18%, +0.58%] |
| runner-primary-final | prior-on → head-on | 20 | 142.96 MiB | 143.41 MiB | +0.44% [+0.28%, +0.60%] |
| runner-primary-final | head-off → head-on | 20 | 143.27 MiB | 143.41 MiB | +0.05% [-0.23%, +0.34%] |
| runner-primary | base-off → head-on | 20 | 143.12 MiB | 142.86 MiB | -0.17% [-0.36%, +0.01%] |
| runner-primary | prior-on → head-on | 20 | 143.08 MiB | 142.86 MiB | -0.10% [-0.35%, +0.15%] |
| runner-primary | head-off → head-on | 20 | 142.77 MiB | 142.86 MiB | +0.16% [-0.03%, +0.35%] |
| runner-slow | base-off → head-on | 4 | 147.34 MiB | 148.55 MiB | +1.13% [-0.70%, +3.00%] |
| runner-slow | head-off → head-on | 4 | 148.38 MiB | 148.55 MiB | +0.18% [-0.34%, +0.71%] |
| runner-unreachable | base-off → head-on | 4 | 141.53 MiB | 142.67 MiB | +0.32% [-1.93%, +2.63%] |
| runner-unreachable | head-off → head-on | 4 | 142.60 MiB | 142.67 MiB | -0.48% [-2.07%, +1.13%] |

## Native Elasticsearch receipts

The measurement relay forwards each request to the actual local Elasticsearch instance and counts successful per-item bulk results. The slow condition holds a received bulk for one second before forwarding. Server receipts after process exit are recorded separately from wall timing. A refused connection yields no receipt. Each measured execution is expected to produce one start and one terminal when usage is enabled. The denominator includes every expected event. Wire sizes are compressed HTTP body bytes; decoded bulk bytes are recorded separately. Elasticsearch storage bytes depend on mappings and retention. The relay belongs to the experiment. The product uses the configured native logger directly.

| Scenario | Candidate | Starts indexed / expected | Terminals indexed / expected | Median compressed bulk bytes |
|---|---|---:|---:|---:|
| runner-empty-sources | head-on | 100 / 100 | 100 / 100 | 1,074 |
| runner-empty-sources | next-on | 100 / 100 | 100 / 100 | 1,072 |
| runner-final-slow | head-on | 15 / 15 | 15 / 15 | 1,070 |
| runner-incremental | head-on | 300 / 300 | 300 / 300 | 1,070 |
| runner-primary-final | head-on | 100 / 100 | 100 / 100 | 1,066 |
| runner-primary-final | prior-on | 100 / 100 | 100 / 100 | 1,368 |
| runner-primary | head-on | 100 / 100 | 100 / 100 | 1,066 |
| runner-primary | prior-on | 100 / 100 | 100 / 100 | 1,376 |
| runner-slow | head-on | 20 / 20 | 20 / 20 | 1,066 |

## Five-plan Runner phase results

**The study did not establish the requested 2% whole-process ceiling.** Forty paired Windows CLI runs measured a geometric wall-time change of **+4.04%** with usage enabled, with a two-sided 95% interval of **−5.84% to +14.96%**. The complete CLI averaged 3,084.71 ms disabled and 3,237.05 ms enabled. The prespecified comparison after the first execution marker measured **+79.73 ms** (+18.14 to +141.32 ms), equivalent to **+2.58% of full disabled wall time** (+0.64% to +4.53%).

All 80 timed launches used canonical commit `320b05b7e7ce080bf1f75a3b4e49706edd5ae473`. The runtime, entry and Framework files matched the same three pinned SHA-256 digests in both arms. An additional enabled and disabled launch served as primers. Every timed launch completed one `act` and four `run` plans, exited zero and passed receipt, marker and diagnostic checks. All 40 physical pairs enter the analysis. Confidence calculations group adjacent opposite-order pairs into 20 randomized order-balanced blocks.

| Observed wall interval | Disabled mean | Enabled mean | Paired absolute change, 95% CI |
|---|---:|---:|---:|
| Launch to first execution, before the explicit usage branch | 2,754.28 ms | 2,826.89 ms | +72.61 ms [−255.06, +400.28] |
| First execution through completion of all five plans | 167.68 ms | 180.17 ms | +12.49 ms [−27.63, +52.62] |
| End of plans to final CLI diagnostic | 122.85 ms | 176.34 ms | +53.50 ms [+14.02, +92.97] |
| Final CLI diagnostic to actual process exit | 39.90 ms | 53.64 ms | +13.74 ms [+3.56, +23.92] |
| First execution to actual exit | 330.43 ms | 410.15 ms | +79.73 ms [+18.14, +141.32] |

The negative control before the first execution changed by +2.35% of full disabled time [−8.38%, +13.08%]. The five plans alone changed by +0.41% [−0.90%, +1.71%] on the same denominator. Child CPU after the first execution changed by +50.0 ms [−11.2, +111.2]. The full-process geometric ratio and the secondary arithmetic phase differences use different estimators. The pair-only sensitivity analysis for the full ratio gave +4.04% [−4.15%, +12.94%].

Both arms delivered 400 ordinary Elasticsearch records, with identical message-template histograms in every pair, and 4,880 ordinary console records. The enabled arm delivered 200 starts and 200 finishes with 200 distinct matched attempt IDs. Mean uncompressed bulk size rose from 4,668 to 11,469 bytes and compressed request bodies from 599 to 1,130 bytes. The relay received the last successful bulk acknowledgement after plan completion in every timed process.

An exploratory split of the longer teardown measured +25.21 ms [+0.68, +49.74] from plan completion to the last bulk acknowledgement and +28.29 ms [+3.51, +53.06] from that acknowledgement to the final CLI diagnostic. The change in summed relay request duration was +13.41 ms [−8.41, +35.22]. These markers include native provider work, scheduling and shutdown. They do not identify a specific change that would retain delivery while eliminating the remaining cost.


## Interpretation of the build comparisons

The earlier PR reported a 21.2% median batch slowdown. The new 20-block test of the selected commit against the merge base had medians of 2.811 s and 2.723 s and a paired change of −2.55% [−13.66%, +9.98%]. The same final binary with usage off and on gave −5.28% [−21.07%, +13.68%]. An independent 40-pair phase study of that binary did not confirm the 2% ceiling. Large startup variation and measurable work after plan completion both contribute. Finishing before Elasticsearch acknowledges writes could invalidate the ordinary debugging and lifecycle receipts.

Use the following mapping to read the tables. In `runner-empty-sources`, `next-on/off` is the selected commit, `head-on` is intermediate `37971206388a1e976d32e61736b23c3968062d57`, and `base-off` is the true merge base. The measured final-on versus intermediate-on improvement was −9.59% [−18.99%, +0.91%]. In `runner-final-*` the head is the selected commit. In `runner-primary`, the head was `35938f8a73844acbf2e806718a727c18a6fc4365`. The primary-final, incremental, A/A, Linux and first Windows endpoint controls used `3797120`. The before files measure the earlier PR.

At intermediate 3797120 the initial 20-pair result against the base was −5.43% [−11.11%, +0.62%]. The next 60 independent blocks gave +2.35% [−3.84%, +8.93%]. Pooling the 80 intermediate blocks gave +0.35% [−4.51%, +5.45%]. The eight same-binary off/off controls measured +8.35% [−8.11%, +27.75%]. A separate 15-block Linux sensitivity run showed a similarly wide interval. All intermediate results are retained separately; they cannot establish performance at the selected head.

## Commands for the measured Runner comparisons

For the final five-plan study, build the two intermediate repositories independently, then stage the selected release host directory in `next-Runner.txt` (one absolute path). For the four-arm command the `Runner` checkout contains 3797120 and `RunnerBase` contains the merge base. For the phase and endpoint studies restore `Runner` to 320b05b and build Release. The phase script validates the Windows binaries against the digests recorded here. A fresh build with different digests needs its own recorded registration in the harness.

```powershell
python paired.py Runner healthy 20 replication-batch.jsonl base-off head-on next-on next-off
python supplemental-source/runner_phase.py --candidate 320b05b --lab .. --host ../Runner/QaaS.Runner.E2ETests/bin/Release/net10.0/QaaS.Runner.E2ETests.dll --output replication-phases
python supplemental-source/runner_phase_report.py replication-phases
foreach ($mode in 'disabled','unreachable','slow') { python paired.py Runner $mode 3 "replication-$mode.jsonl" base-off head-off head-on }
python high.py --rounds 3 --modes healthy,slow
```

## Cross-product high-assertion workload

Each Runner launch made a witnessed successful HTTP call to Mocker, executed all 128 named assertions and returned the expected failure exit code. Each mode had three alternating pairs on the selected binaries with usage off/on. Values below are marginal medians. The paired wall column gives estimate / lower / upper 95% limit.

| Endpoint | Median wall off / on (s) | Assertions/s off / on | Median child CPU off / on (s) | Paired wall change |
|---|---:|---:|---:|---:|
| healthy | 9.41 / 5.14 | 13.60 / 24.91 | 9.00 / 6.38 | -28.74% / -60.16% / +27.46% |
| slow | 10.95 / 11.11 | 11.69 / 11.52 | 7.33 / 7.55 | -2.31% / -16.01% / +13.62% |

## Functional verification at the measured commit

The real Runner-to-Mocker fixture produced six starts and six finishes. Five succeeded and one failed an assertion. Both HTTP calls originating from Runner returned the expected 200 response and body. All 324 dashboard comparisons across 12 filter sets agreed with the local oracle. On an additional pass, the success and failure fixtures retained the same ordinary debug lines in the same order as the merge base after normalizing timestamps and generated IDs. The archive contains the per-filter counts and debug receipts.

All 1,122 Release tests passed. Push and pull-request CI passed at this SHA and the review-thread audit found zero unresolved threads. Human review is pending. [Pull-request CI](https://github.com/TheSmokeTeam/QaaS.Runner/actions/runs/45574875789)

## Reproduce

On Windows, extract measurements.zip into an empty directory. Create repository checkouts named `Runner`, `RunnerBase`, `Mocker` and `MockerBase` beside `perf-followup`. Check out the candidate and merge-base SHAs and build each Release solution with a single MSBuild node. Restore the same packages recorded in the original report. Build the prior PR in a separate checkout and copy its entire `net10.0` Release host output into `perf-followup/before-Runner`. The Runner host project is `QaaS.Runner.E2ETests` and the Mocker host project is `QaaS.Mocker.Example`.

Start the isolated ES7 container mapped to 127.0.0.1:19278 as described in the [original verification](https://github.com/TheSmokeTeam/QaaS.Runner/blob/ecffc1152c481c1e458605a1544b59c572ed2530/verification.md). The refusal case expects port 19996 to be unused. From the extracted archive root, use `python reproduce.py` to generate the fixed batch fixture and rewrite its absolute paths for your checkout. Execute the benchmark scripts from `perf-followup`. The original observations are kept under `measurements` and `checks`, separate from new output. Builds and downloads occur outside timed intervals. The labelled unprimed calibration includes its cold launch.

```powershell
dotnet build QaaS.Runner.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test QaaS.Runner.sln -c Release --no-build --no-restore -m:1
python suite.py Runner primary
```

The full paired statistics, CLI command construction, fixed fixture, seed and individual measurements are in the downloadable archive. The source refs above identify the product code.

For the live comparison, use the isolated Docker network, Elasticsearch transforms, Grafana data sources and dashboards described in the original verification. The test harness expects the local `mamtzetza-mocker` runtime image, an attached `qaas-pr45-local` network, the ES container `qaas-pr78-es`, Grafana on port 13045 and Elasticsearch on port 19278. From `revision-verification`, run `python live.py`, `python check_final_live.py` and `python debug_check.py` in order. The window that extends 20 minutes past the last event tests missing finishes after the dashboard’s 15-minute grace period. The archive includes the expected event counts, filter comparison results and local workload drivers.
