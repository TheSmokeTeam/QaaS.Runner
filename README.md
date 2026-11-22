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

## Optional local attempt summary

A direct top-level `run`, `act`, or `assert` started with `Bootstrap.New` can hand one compact terminal summary to an **operator-provisioned, private local directory**. It is off by default and does not send data over the network or change result reporting. Multiple cases within the same command are part of one attempt. `execute` (including its nested commands), `template`, help, version, and argument parse failures do not emit summaries.

Set all three environment variables on the specific invocation:

| Variable | Value |
| --- | --- |
| `QAAS_ATTEMPT_SUMMARY_DIRECTORY` | Absolute, existing directory on a healthy local filesystem, scoped to trusted producer and collector. Do not use shared/untrusted directories, network mounts or UNC paths. Runner does not create the directory. |
| `QAAS_ATTEMPT_ID` | Externally assigned, globally unique opaque 32 lowercase hex characters, not all zero. Do not use the same ID for a new attempt, including a business retry. |
| `QAAS_PROJECT_KEY` | Independently provisioned opaque project identifier in the same 32-character format. The trusted launch process owns project authorization and correlation. Runner validates the shape, not project ownership. |

Example (synthetic IDs):

```bash
QAAS_ATTEMPT_SUMMARY_DIRECTORY=/var/spool/qaas-attempts \
QAAS_ATTEMPT_ID=0123456789abcdef0123456789abcdef \
QAAS_PROJECT_KEY=abcdef0123456789abcdef0123456789 \
dotnet run -- run test.qaas.yaml
```

Runner makes one best-effort, create-if-absent publication of `<attempt_id>.json`. The collector should ignore temporary files, independently authenticate the producer/project, and deduplicate by attempt ID. Runner does not overwrite an existing terminal, retry, force an `fsync`, invoke a collector, or log the payload. A full/permission-denied/unavailable spool leaves the original return code or exception unchanged. An invalid or incomplete opt-in is ignored. Publication failure is intentionally silent at this layer: use an independent ledger of mature expected attempts to detect gaps, alongside collector and spool monitoring. Configure disk quotas/retention and access control separately. Local filesystem operations are synchronous and can still block on an unhealthy filesystem; there is no hard latency bound or crash-durability guarantee. Abrupt termination before publication may leave no terminal summary.

The file is one UTF-8 JSON object capped at 1 KiB:

- `schema_version`: integer `1`; `attempt_id` and `project_key`: the opaque keys above.
- `operation`: `run`, `act`, or `assert`.
- `outcome`: `pass` (nonempty fully passed assertions), `assertion_failure` (definite failed assertion results without an indeterminate or broken result), `infra_failure` (loading/lifecycle/cleanup failure or broken assertion), `cancelled` (observed cancellation exception), `completed` (successful `act` or success with no assertion evidence), or `unknown` (indeterminate/custom outcome). `completed` is not a passed test, and an exit code alone does not prove assertion failure.
- `completed_at`: UTC timestamp, milliseconds; `duration_ms`: elapsed monotonic milliseconds from opted-in parsed command load through terminal lifecycle/logging, before writing the file; `exit_code`: integer normally returned by Runner or `null` when an exception propagates.

No raw assertion, case, path, reporter, exception, payload, or user data is emitted. Opaque keys are pseudonymous operational identifiers, not metric labels or proof of personal use.

For optional Elasticsearch delivery and dashboard setup, follow the [out-of-process collector guide](tools/observability/README.md). Run its setup canary against the actual deployed Runner build before enabling normal collection.

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
