# QaaS.Runner

Execution orchestration package for running QaaS test workflows from YAML configuration.

[![CI](https://img.shields.io/badge/CI-GitHub_Actions-2088FF)](./.github/workflows/ci.yml)
[![Docs](https://img.shields.io/badge/docs-qaas--docs-blue)](https://thesmoketeam.github.io/qaas-docs/)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

## Contents
- [Overview](#overview)
- [Packages](#packages)
- [Projects](#projects)
- [Quick Start](#quick-start)
- [ReportPortal](#reportportal)
- [E2E Validation](#e2e-validation)
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
- Orchestrates setup/teardown, optional Allure result serving, and optional ReportPortal launch finalization.

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

## ReportPortal
QaaS can publish the same assertion results it already writes to Allure into an existing ReportPortal instance.

Runtime rules:
- QaaS never creates ReportPortal projects, dashboards, filters, users, or API keys.
- Project routing is derived only from `MetaData.Team`, case-insensitively.
- Launches are grouped by `Team + System`.
- `ExtraLabels` and other metadata key/value pairs are emitted as ReportPortal attributes so teams can filter by labels such as `Component`, `Area`, or `Owner`.
- Each assertion is published as its own ReportPortal test item together with assertion message/trace, stack trace for broken assertions, session summaries, session failure history, assertion attachments, template YAML, and a generated assertion-context JSON artifact.
- Assertion links configured in QaaS remain active in Allure and are also written into ReportPortal logs.
- When ReportPortal is enabled but the endpoint, API key, or target project is invalid, QaaS logs a warning and skips ReportPortal publishing without crashing the runner or changing the exit code for otherwise passing runs.
- The default launch name is stable and derived from `Team + System + Sessions`, which keeps ReportPortal history grouped without creating new dashboards or projects.

Environment variables:

| Variable | Default | Notes |
|---|---|---|
| `QAAS_REPORTPORTAL_ENABLED` | `true` | Enables best-effort ReportPortal publishing. |
| `QAAS_REPORTPORTAL_ENDPOINT` | none | Required when reporting is enabled. Must point to the ReportPortal base URL or API URL. |
| `QAAS_REPORTPORTAL_API_KEY` | none | Required when reporting is enabled. Must already have write access to the target team projects. |
| `QAAS_REPORTPORTAL_PROJECT` | none | Ignored at runtime. Kept only so QaaS can warn when callers try to override project routing manually. |

Notes:
- `QAAS_REPORTPORTAL_ENDPOINT` and `QAAS_REPORTPORTAL_API_KEY` are environment-driven at runtime. QaaS does not fall back to YAML values for them.
- Launch names are derived from the grouped team, system, and sessions unless you explicitly override the launch name/description in YAML.
- Allure remains active and unchanged when ReportPortal publishing is enabled.

## E2E Validation
[`QaaS.Runner.E2ETests`](./QaaS.Runner.E2ETests/) contains ReportPortal-focused scenarios under [`Configs/ReportPortal`](./QaaS.Runner.E2ETests/Configs/ReportPortal/) and a single [`executable.yaml`](./QaaS.Runner.E2ETests/executable.yaml) that groups them by command ID.

Run the following commands from [`QaaS.Runner.E2ETests`](./QaaS.Runner.E2ETests/):

Useful commands:

```bash
cd ./QaaS.Runner.E2ETests
dotnet run -- execute executable.yaml -c SmokeQaaS
dotnet run -- execute executable.yaml -c SmokeQaaSStress
dotnet run -- execute executable.yaml -c SmokeQaaS -c SmokeSmoothStress
dotnet run -- execute executable.yaml -c TactiCrawler -c WonderYoungStress
dotnet run -- execute executable.yaml -c SmokeQaaSStress -c TactiMifal -c WonderBritianStress
dotnet run -- execute executable.yaml -c MissingProject
dotnet run -- execute executable.yaml -c CaseInsensitiveTeam
dotnet run -- execute executable.yaml -c InvalidApiKey
```

Warning-path checks:
- `MissingProject` uses a non-existent team/project mapping and should warn while preserving a zero exit code.
- `MissingEndpoint` uses a pass-only scenario and should warn when `QAAS_REPORTPORTAL_ENDPOINT` is unset.
- `InvalidApiKey` uses a pass-only scenario and should warn when `QAAS_REPORTPORTAL_ENDPOINT` points to a live instance but `QAAS_REPORTPORTAL_API_KEY` is invalid.

Team/system matrix available through `executable.yaml`:
- `SmokeQaaS`
- `SmokeQaaSStress`
- `SmokeSmooth`
- `SmokeSmoothStress`
- `TactiCrawler`
- `TactiCrawlerStress`
- `TactiMifal`
- `TactiMifalStress`
- `WonderYoung`
- `WonderYoungStress`
- `WonderBritian`
- `WonderBritianStress`

Scenario layout:
- `Configs/ReportPortal/Teams/Smoke`, `Configs/ReportPortal/Teams/Tacti`, and `Configs/ReportPortal/Teams/Wonder` each contain baseline and stress scenarios for the systems owned by that team.
- Baseline scenarios exercise links, message/trace enrichment, session exports, and attachment fan-out.
- Stress scenarios add extra data sources, more sessions, deliberate session failures, larger traces, more attachments, and additional metadata labels such as `Scenario`, `ReleaseRing`, and `Tenant`.

## Documentation
- Official docs: [thesmoketeam.github.io/qaas-docs](https://thesmoketeam.github.io/qaas-docs/)
- CI workflow: [`.github/workflows/ci.yml`](./.github/workflows/ci.yml)
- NuGet package page: [QaaS.Runner on NuGet](https://www.nuget.org/packages/QaaS.Runner/)
