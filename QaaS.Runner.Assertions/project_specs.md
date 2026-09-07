# project_specs.md — QaaS.Runner.Assertions

> Per-project specification. For the solution-level spec see
> `../project_specs.md`. For the AI operating manual see `../CLAUDE.md`.

## Role

Assertion engine and reporter dispatcher.

- Builds `IAssertion` hooks (resolved from `QaaS.Framework.Providers`) into
  runtime `Assertion` objects.
- Runs them against the assembled `IImmutableList<SessionData>` collected
  by the session runtime.
- Routes results through the configured `IReporter` implementations.

## Key types

| Type | Purpose |
|---|---|
| `IReporter` | Abstraction for downstream reporting backends. |
| `BaseReporter` | Shared scaffolding — open/close test, attach files, hash-dedup. |
| `AllureReporter` (~35 KB) | Allure-format JSON + attachments. Attachments deduplicated via `ConcurrentDictionary`. |
| `ReportPortalReporter` | Passive ReportPortal reporter that queues assertion-result references for runner-owned final publishing. |
| `AssertionObjects/*` | Builder + runtime wrapper around an `IAssertion` hook. |
| `LinkBuilders/*` | Builders that compose Grafana, Kibana, and Prometheus links into the report. |
| `ConfigurationObjects/*` | YAML-bound config records for assertions and reporters. |

## Reporter selection

`ReporterBuilder` creates the configured reporter instances for the execution.
`ReportLogic` routes each reportable assertion result to built reporters whose
type is listed on the runtime assertion's `ReporterTypes`.
Allure writes immediately. ReportPortal queues locally during execution; one
runner-owned publisher validates all launch groups read-only immediately before
the first ReportPortal-enabled `run` or `assert` and publishes grouped launches
at cleanup. `template` and `act` do not build reporters.

## Concurrency

- Multiple assertions can run sequentially or in parallel depending on
  configuration; reporters must therefore be thread-safe at the public API
  boundary.
- Allure attachment dedup: `ConcurrentDictionary<hash, path>`.

## Forbidden in this project

- Hard-coding "Allure" anywhere outside `AllureReporter`.
- Mutating `SessionData` from inside an assertion (it's an immutable input).
- Throwing arbitrary exceptions from `IReporter` methods — wrap in a typed
  exception so `ReportLogic` can localise the error.

## Tests

`QaaS.Runner.Assertions.Tests` — covers reporter dispatch, link builder
output, `AllureReporter` attachment dedup, and assertion result mapping.
