# QaaS.Runner

Execution orchestration package for running QaaS test workflows from YAML configuration.

[![CI](https://github.com/TheSmokeTeam/QaaS.Runner/actions/workflows/ci.yml/badge.svg)](https://github.com/TheSmokeTeam/QaaS.Runner/actions/workflows/ci.yml)
[![CI](https://img.shields.io/badge/CI-GitHub_Actions-2088FF)](./.github/workflows/ci.yml)
[![Docs](https://img.shields.io/badge/docs-qaas--docs-blue)](https://thesmoketeam.github.io/qaas-docs/)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

## Contents
- [Overview](#overview)
- [Packages](#packages)
- [Projects](#projects)
- [Quick Start](#quick-start)
- [ReportPortal](#reportportal)
- [Documentation](#documentation)

## Overview
This repository contains one solution: [`QaaS.Runner.sln`](./QaaS.Runner.sln).

`QaaS.Runner` is published to NuGet and includes the runner runtime plus packaged project outputs from this solution that are required at runtime (sessions/assertions/storage orchestration flow).

## Packages
| Package | Latest Version | Total Downloads |
|---|---|---|
| [QaaS.Runner](https://www.nuget.org/packages/QaaS.Runner/) | [![NuGet](https://img.shields.io/nuget/v/QaaS.Runner?logo=nuget)](https://www.nuget.org/packages/QaaS.Runner/) | [![Downloads](https://img.shields.io/nuget/dt/QaaS.Runner?logo=nuget)](https://www.nuget.org/packages/QaaS.Runner/) |

## Projects
### [QaaS.Runner](./QaaS.Runner/)
- CLI/bootstrap entrypoint for `run`, `act`, `assert`, `template`, and `execute` verbs.
- Builds execution contexts and routes each execution type through the right logic chain.
- Orchestrates setup/teardown, optional Allure result serving, ReportPortal access validation, and final ReportPortal publishing.

### [QaaS.Runner.Assertions](./QaaS.Runner.Assertions/)
- Builds assertion runtime objects from configured hooks and filters.
- Executes assertions against session/data-source outputs.
- Writes Allure results, links, and attachments.
- Can also publish the same runner-produced assertion results into ReportPortal.

### [QaaS.Runner.Sessions](./QaaS.Runner.Sessions/)
- Session runtime with staged action execution.
- Supports publishers, consumers, transactions, probes, and collectors.
- Produces session data and failure/flakiness metadata.

### [QaaS.Runner.Storage](./QaaS.Runner.Storage/)
- Storage abstraction for storing and retrieving serialized session data.
- Built-in implementations: filesystem and S3-compatible backends.
- Shared builder-based configuration for act/assert flows.

### [QaaS.Runner.Infrastructure](./QaaS.Runner.Infrastructure/)
- Small shared cross-project helpers (filesystem and date/time utilities).

## Quick Start
Install package:

```bash
dotnet add package QaaS.Runner
```

Upgrade package:

```bash
dotnet add package QaaS.Runner --version <target-version>
dotnet restore
```

## Execution observability

Enable existing Elasticsearch logging with `--send-logs --elastic-uri <url>` or the configured defaults. A custom logger must admit Debug events. Set `MetaData.Team`, `MetaData.System` and the string `MetaData.ExtraLabels.Project` to trusted organizational labels. Missing ownership appears under `(unattributed)`. The framework classifies Kubernetes executions as CI and other executions as Local.

Runner measures each built `run`, `act` or `assert` invocation, including cases and commands within `execute`. A full `run` is one test-plan execution. A finish records its monotonic duration, outcome and known assertion-result count. Interrupted parallel assertions can leave that count unknown. Each measured invocation has a fresh attempt ID. Setup failures before the execution starts and template commands fall outside the lifecycle counts.

1. Open the [Elasticsearch configuration](https://raw.githubusercontent.com/TheSmokeTeam/QaaS.Runner/320b05b7e7ce080bf1f75a3b4e49706edd5ae473/observability/elasticsearch.console) in Kibana Dev Tools. Install its shared ingest pipeline and mapping component. Add `qaas-usage-fields-v1` to the template for future daily logs, or copy its two field mappings into the existing legacy template. Apply the same mappings to current `qaas-20*` log indices before enabling the pipeline.
2. Configure `qaas-usage-stamp-v1` as `index.final_pipeline` in the log template and current index settings. If a final pipeline is already in use, append a `pipeline` processor calling `qaas-usage-stamp-v1` to it. Preserve the existing processors. Run the remaining Console requests to create the product destination, create the continuous transform and start it. The stated resources require Elasticsearch ingest, transform and index privileges. The transform reads stamped logs from installation onward. Keep raw logs for at least the longest run plus the retry window, and size destination retention for the usage history you want to query.
3. Configure Grafana’s Elasticsearch data source with UID `qaas-runner-usage-v1`, index `qaasusage-runner-v1` and time field `event.Time`. Import or file-provision the [Runner dashboard](https://raw.githubusercontent.com/TheSmokeTeam/QaaS.Runner/320b05b7e7ce080bf1f75a3b4e49706edd5ae473/grafana/runner-usage.json). Check `GET /_transform/qaasusage-runner-v1/_stats` for checkpoint progress. The transform checks every 30 seconds with a 10-second ingestion delay.

Set `QAAS_USAGE_ENABLED=false` to disable these events while retaining ordinary diagnostics. To suspend analytics, stop the transform with `POST /_transform/qaasusage-runner-v1/_stop`. Existing raw logs remain available.

## ReportPortal
QaaS can send its assertion results to an existing ReportPortal instance. Configure `Reporters.ReportPortal` with `Enabled`, `Endpoint`, `ApiKey`, and an optional `Project` (defaults to `MetaData.Team`). YAML values override package defaults.

- QaaS does not create ReportPortal projects or credentials; the target project and API key must already exist.
- Only `run` and `assert` use ReportPortal. `template` still renders its configuration, while `template` and `act` perform no validation or publishing.
- Immediately before the first ReportPortal-enabled `run` or `assert`, the runner validates every enabled launch group with a read-only project lookup. If validation fails, it returns exit code `1` and skips that command and everything after it; earlier commands remain completed.
- Assertion results are queued locally and published during cleanup. Compatible ReportPortal configurations share a launch.
- Final publishing errors are logged without changing the execution-derived exit code. Allure output is unaffected.

## Documentation
- Official docs: [thesmoketeam.github.io/qaas-docs](https://thesmoketeam.github.io/qaas-docs/)
- CI workflow: [`.github/workflows/ci.yml`](./.github/workflows/ci.yml)
- NuGet package page: [QaaS.Runner on NuGet](https://www.nuget.org/packages/QaaS.Runner/)
