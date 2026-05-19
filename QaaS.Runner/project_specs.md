# project_specs.md — QaaS.Runner (CLI + Bootstrap)

> Per-project specification for the **QaaS.Runner** project (the CLI bootstrap
> and central composition root). For the solution-level spec see
> `../project_specs.md`. For the AI operating manual see `../CLAUDE.md`.

## Role

Top-level project of the Runner solution. Owns:

- CLI parsing (sub-commands `run`, `act`, `assert`, `execute`, `template`).
- YAML loading via `RunLoader` (delegates to
  `QaaS.Framework.Configurations`).
- The `RunnerExecutionBuilder`-rooted graph composition (`ExecutionBuilder.cs`,
  ~60 KB — the single most important file in the solution).
- High-level orchestration logics under `Logics/` (`SessionLogic`,
  `AssertionLogic`, `StorageLogic`, `ReportLogic`).
- Autofac module registration under `Modules/`.

## Key types

| Type | File | Purpose |
|---|---|---|
| `Bootstrap` | `Bootstrap.cs` | Static `New(args)` factory. Builds the Autofac container, wires modules, returns a `Runner`. |
| `Runner` | `Runner.cs` | `Run()` / `RunAndGetExitCode()` — drives the configured execution to completion. |
| `Execution` | `Execution.cs` | Encapsulates a built run; disposable. |
| `ExecutionBuilder` | `ExecutionBuilder.cs` | The fluent root builder. `Build()` calls `BuildDataSources/Sessions/Assertions/Storages/Links` in order. |
| `IExecutionBuilderConfigurator` | same name | Plug-in seam letting host apps mutate the builder before `Build()`. |
| `RunLoader` | `Loaders/` | YAML → builders deserialisation, placeholder + reference resolution, validation. |
| `SessionLogic` / `AssertionLogic` / `StorageLogic` / `ReportLogic` | `Logics/` | Phase-specific orchestrators invoked by `Runner.Run()`. |
| `RunnerDiagnosticMessageFormatter` | root | Pretty-prints YAML/validation errors. |
| `RunnerYamlConfigurationExceptionFactory` | root | Wraps low-level YAML failures with line/column context. |

## CLI surface

Defined under `Options/` and routed by the bootstrap. Every option carries
help text; verb classes inherit a common base for shared options
(`--configuration-path`, `--variables`, `--reporters`, `--verbose`).

## Modules

`Modules/` registers:

- Hook providers (`IHookProvider<IGenerator>`, `<IAssertion>`, `<IProbe>`,
  `<IProcessor>`).
- Reporter implementations.
- Storage implementations.
- Logger (Serilog) per `QaaS.Framework.Executions.ExecutionLogging`.
- Configuration loaders (YAML + placeholder/reference parsers).

## Conventions

- All public builder methods use the partial-class pattern
  (`*Properties`/`*Logic`/`*Validation`). The `ExecutionBuilder` itself
  follows the same split.
- Diagnostics: prefer typed exceptions converted by
  `RunnerYamlConfigurationExceptionFactory` over ad-hoc `throw new
  Exception(...)`.

## Forbidden in this project

- Hard-coding hook references — always go through the providers.
- Putting protocol logic here — it belongs in `QaaS.Framework.Protocols`.
- Bypassing `RunLoader` for a quick "load YAML" — duplicates placeholder
  resolution and breaks validation.

## Tests

`QaaS.Runner.Tests` covers Bootstrap wiring, CLI option parsing,
`ExecutionBuilder` smoke tests, and `RunLoader` round-trips.
