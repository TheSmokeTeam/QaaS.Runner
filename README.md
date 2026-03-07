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

## Documentation
- Official docs: [thesmoketeam.github.io/qaas-docs](https://thesmoketeam.github.io/qaas-docs/)
- CI workflow: [`.github/workflows/ci.yml`](./.github/workflows/ci.yml)
- NuGet package page: [QaaS.Runner on NuGet](https://www.nuget.org/packages/QaaS.Runner/)
- ReportPortal integration: [`docs/reportportal-integration.md`](./docs/reportportal-integration.md)
