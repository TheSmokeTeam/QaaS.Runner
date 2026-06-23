# CLAUDE.md — QaaS.Runner (Main CLI/Bootstrap Project)

## Purpose
CLI entrypoint and execution orchestrator. Handles `run`, `act`, `assert`, `template`, and `execute` verbs. Loads YAML configurations, prepares execution builders, builds execution contexts, and routes through the pipeline.

## Key Files
- `Bootstrap.cs` — CLI argument parsing, DI setup, verb routing
- `ExecutionBuilder.cs` — Builds the full execution graph from YAML config (sessions, data sources, storages, assertions)
- `Runner.cs` — Main orchestration loop: prepares builders, runs sessions by stage, handles storage, assertions, and ReportPortal launch teardown
- `Execution.cs` — Execution context holder
- `Constants.cs` — YAML path constants, supported reference lists

## Modules & Directories
- `Logics/` — Domain logic classes (`SessionLogic`, `AssertionLogic`, `StorageLogic`, `DataSourceLogic`, `ReportLogic`, `TemplateLogic`)
- `Loaders/` — Configuration file loaders
- `Options/` — CLI option classes per verb
- `Modules/` — DI module registrations, including `ReportPortalLaunchManagerModule`
- `ConfigurationObjects/` — Runner-level configuration models
- `Extensions/` — Helper extensions

## Build
```bash
dotnet build QaaS.Runner/QaaS.Runner.csproj
```
