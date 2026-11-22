# Optional Runner terminal KPI read model

This read-only materializer turns **accepted** V1 Runner terminal JSONL into aggregate Prometheus gauges and an importable Grafana dashboard. It is not the collector, a Runner dependency, or a replacement for existing Elastic dashboards. It does not contact any external endpoint. The optional local exporter serves the last atomically published aggregate on loopback only.

## Use

Python 3.11+ standard library only. Obtain an immutable **accepted-only** export from an authorized, point-in-time read of the agreed concrete collector index. Do not use queued, rejected, pending, mixed-schema or arbitrary diagnostic logs. No automatic production export, production credentials or scheduling are included. Roster and ledger must originate independently of the telemetry whose completeness they measure. Choose a UTC cutoff behind known ingestion delay, and schedule materialization with an approved scheduler. Invalid input preserves the last snapshot; monitor displayed cutoff for staleness.

Example, using only the included synthetic fixture:

    python -m unittest -v test_readmodel
    python readmodel.py --accepted fixtures/accepted.jsonl --roster fixtures/roster.json --ledger fixtures/ledger.json --as-of 2026-11-22T16:00:00Z --output /tmp/qaas-runner-kpi.prom
    python make_dashboard.py --roster fixtures/roster.json --as-of 2026-11-22T16:00:00Z --output grafana/dashboards/runner-usage.json
    python serve.py --metrics /tmp/qaas-runner-kpi.prom

Use the loopback Prometheus scrape sample and Grafana provisioning examples, or import the generated dashboard in an explicitly approved Grafana with datasource UID qaas-runner-kpi. To provision, point GF_PATHS_PROVISIONING to the provisioning directory and QAAS_KPI_DASHBOARD_DIR to the dashboard directory. Promote generated files under the required service-reader ownership: the materializer writes private 0600. Use one logical scrape target (or externally deduplicated replicas). Do not sum all with its constituent scopes, or average ratios or percentiles across scopes. Grafana's time picker does not change these exact snapshot windows.

The real API verifier runs disposable local services in a private namespace, without live auth or changes to existing services:

    unshare --user --map-root-user --net python grafana/offline_loopback.py python grafana/smoke.py --grafana-home /path/to/grafana --prometheus /path/to/prometheus

## Contracts and interpretation

- **Accepted JSONL:** schema_version (integer 1); attempt_id and project_key (nonzero 32 lowercase hex); operation (run/act/assert); outcome (pass/assertion_failure/infra_failure/cancelled/completed/unknown); completed_at (UTC Z, optional 1–7 fractional digits); duration_ms (nonnegative Int64); exit_code (Int32 or null). Repeating the same semantically identical terminal attempt is idempotent. A different payload using the same attempt ID, including another project, fails closed. The caller allocates a globally unique ID per business attempt. Exit code is validated but never exposed or used as a substitute for the reported outcome.
- **Independent roster:** schema_version 1, roster_id, valid_from and valid_until, allowlisted teams/environments, projects with project_key, team, environment, eligible_from, nullable exclusive-end ineligible_at, and boolean enrolled. This models present assignment and one interval per project, not arbitrary historical team changes.
- **Optional independent expected ledger:** schema_version 1, ledger_id, valid_from and valid_until, attempts with scheduled attempt_id, project_key, due_at, mature_at (due ≤ mature). Roster and ledger must cover the full 30-day report and be valid at cutoff. Unknown or mismatched ledger project is invalid, not a guessed join.
- **Adoption:** each window is UTC inclusive-start/exclusive-end. Only projects independently eligible at cutoff and observed after eligibility began enter adoption. Unrostered and known out-of-cohort terminal events appear only in all/all quality aggregates. An empty trusted export gives observed zero for a nonempty roster, not proof of inactivity. An empty eligible cohort emits no adoption ratio; enrollment is independent. The absent ledger is shown separately and produces no ledger-derived values, including no unexpected count.
- **Operation and outcomes:** Run does not imply observed Act or Assert. Successful completed is known platform completion, not a test pass. Functional test denominator is pass plus assertion failure. Infrastructure failure and exact nearest-rank duration denominators use pass, assertion_failure, infra_failure and completed, not cancelled or unknown. Missing events do not become failures.
- **Ledger matching:** only mature_at strictly before the cutoff counts as mature. The same project and attempt must have expected due and observed terminal timestamps inside the selected window. An immature matching observation has its own tally, not unexpected. Platform-good match means pass, assertion failure or successful completion. Unknown and cancelled may count as covered but not good, passed or inferred failures.
- **Bounds and privacy:** 64 MiB terminal JSONL, 2 KiB lines, 100k terminal/ledger rows, 24 MiB per manifest, 5k roster projects; eight team and four environment allowlist entries at most, plus all groups. Two windows × 45 scopes × 36 families cap one logical target at 3,240 samples, some intentionally absent. Metrics never include attempt/project IDs, exit code, input paths, raw messages or credentials. Keep personal/project identifiers out of allowlisted team/environment labels.

Synthetic fixture reference: 7-day adoption 3/5 and coverage 6/7; 30-day adoption 4/5 and coverage 9/10. Independent files are synthetic examples, not organizational counts.
