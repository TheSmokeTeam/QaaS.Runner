# CLAUDE.md — QaaS.Runner Solution

> Operating manual for AI assistants working in the **QaaS.Runner** repository.
> See `project_specs.md` for the architectural specification, and the
> per-project `project_specs.md` files inside each `QaaS.Runner.*` directory
> for project-level details. Live docs: <https://docs.qaas.online/>.

## Mission
**QaaS.Runner** is the execution-orchestration engine of the QaaS platform.
It loads a YAML test definition (typically `test.qaas.yaml`), resolves hooks
discovered via `QaaS.Framework.Providers`, builds a graph of *Sessions →
Stages → Actions*, executes that graph (with per-action data parallelism),
runs configured assertions over the captured `SessionData`, and emits reports
(Allure, ReportPortal). The runner is the canonical consumer of every
contract defined in `QaaS.Framework`.

## Build / Test / Run

```bash
# Build entire solution
dotnet build QaaS.Runner.sln --nologo -clp:ErrorsOnly

# Run all unit tests (fast, no external deps)
dotnet test QaaS.Runner.sln --nologo --no-build

# Run a specific project
dotnet test QaaS.Runner.Sessions.Tests --nologo --no-build

# Filter
dotnet test QaaS.Runner.Sessions.Tests --filter "FullyQualifiedName~Transaction" --nologo

# Format (run before any commit)
csharpier format <changed-files>

# Run an example test
dotnet run --project QaaS.Runner -- run --configuration-path path/to/test.qaas.yaml
```

E2E tests in `QaaS.Runner.E2ETests` require running infrastructure (RabbitMQ,
SQL, etc.) — they are not part of the default `dotnet test` run unless their
project is explicitly targeted.

## Solution layout

| Project | Purpose |
|---|---|
| `QaaS.Runner` | CLI entrypoint (`Bootstrap.New`), YAML loader, `ExecutionBuilder`, top-level orchestration logics, modules. The 60 KB `ExecutionBuilder.cs` is the central composition root. |
| `QaaS.Runner.Sessions` | Session/Stage/Action runtime. Houses every action type (`Publisher`, `Consumer`, `Transaction`, `Probe`, `Collector`, `MockerCommand`), their builders (split into `*Properties.cs` / `*Logic.cs` / `*Validation.cs` partials), and the `IterableSerializableDataIterator` that drives data-parallel iteration. |
| `QaaS.Runner.Assertions` | Assertion engine. Builds `IAssertion` hooks into runtime objects, executes them against `SessionData`, owns `AllureReporter` (~35 KB) and the `IReporter` abstraction (Allure + ReportPortal). |
| `QaaS.Runner.Storage` | Pluggable session-data persistence (FileSystem, S3). Used by `act`/`assert` CLI flows to decouple acting from asserting. |
| `QaaS.Runner.Infrastructure` | Pure helpers: template rendering, datetime/timezone math, filesystem extensions, context/metadata extensions. Zero behavioural state. |
| `QaaS.Runner.E2ETests` | Real-protocol round-trip tests (RabbitMQ etc.). |
| `QaaS.Runner.*.Tests` | NUnit 4.x + Moq unit tests, one project per non-test project above. |

Internal access between projects relies on `InternalsVisibleTo` declared in
each project's `csproj`. **Do not** widen `internal` to `public` to make a
test pass — add the friend-assembly reference.

## End-to-end execution flow (high level)

```
CLI args
   │
   ▼
Bootstrap.New(args) ──► RunLoader (YAML+placeholders+references+validation)
   │                         │
   │                         ▼
   │                    Deserialised builders (e.g. RunnerExecutionBuilder)
   │                         │
   ▼                         ▼
Runner.Run() ──► ExecutionBuilder.Build()
                      │
                      ├─ BuildDataSources() ── resolve IGenerator hooks
                      ├─ BuildSessions()    ── resolve IProbe + protocol cfgs
                      ├─ BuildAssertions()  ── resolve IAssertion hooks
                      ├─ BuildStorages()
                      └─ BuildLinks()
                      │
                      ▼
                 SessionLogic ── for each Stage:
                                    └ run all Actions concurrently (Task.Run)
                                       └ each Action iterates data with
                                         SemaphoreSlim-bounded parallelism,
                                         locks shared lists, observes Policies
                      │
                      ▼
                 AssertionLogic ── execute assertions over SessionData
                      │
                      ▼
                 StorageLogic  ── persist/restore SessionData (act/assert split)
                      │
                      ▼
                 ReportLogic   ── route reporters by configured type
                                  (AllureReporter, ReportPortalReporter)
                      │
                      ▼
                 Exit code     0 = pass · 1 = fail · 2 = parse/config error
```

## Concurrency model (critical — read before touching it)

Three nested concurrency levels:

1. **Stage-level**: every Action in a Stage is launched via `Task.Run` and
   awaited together at stage end.
2. **Action-level (data parallelism)**: inside a single Action,
   `IterableSerializableDataIterator.ApplyToAll(data, body, parallel: true)`
   uses `Parallel.ForEach` throttled by `SemaphoreSlim(parallelism, parallelism)`.
3. **Session-level**: deferred sessions can overlap stages.

Safety rules:
- Mutating shared lists from inside the parallel body **must** use `lock`.
- Use `Interlocked.Increment` for shared counters.
- `StopActionException` is the *only* sanctioned way to short-circuit a
  parallel iteration (caught by `ApplyToAll`); it is **not** valid outside
  that loop.
- Live action data flows through `RunningCommunicationData<T>` /
  `BlockingCollection<T>` exposed on `InternalContext`.
- Recent change (PR #33 / commit `a6f4c7d`): transactions now also support
  parallelism. If you touch the iterator, mirror behaviour for both
  publishers and transactions, and re-run `QaaS.Runner.Sessions.Tests`.

## Hook discovery

The runner does **not** know the concrete types of generators, assertions, or
probes — they are discovered at startup via `QaaS.Framework.Providers`:

1. Builder requests `IList<KeyValuePair<string, IGenerator>>` (and friends)
   from the Autofac scope.
2. The provider scans loaded assemblies in this priority order: `QaaS.*` →
   `Common.*` → others, then by name.
3. Hooks are matched by simple name first, then by fully-qualified name to
   resolve ambiguity.
4. Each hook gets `Context` injected, then `LoadAndValidateConfiguration`
   is called; failures surface as YAML deserialisation errors.

When adding a new hook surface, **always** prefer extending `QaaS.Framework`
or `QaaS.Common.*` over baking the hook into the runner.

## Reporting

`QaaS.Runner.Assertions` owns reporting:

- `IReporter` is the abstraction.
- `AllureReporter` writes Allure-format JSON + attachments (deduped via
  `ConcurrentDictionary`).
- ReportPortal integration ships as a passive reporter; `ReportLogic`
  queues reportable assertion results locally, while one runner-owned publisher
  validates access read-only before execution and publishes grouped launches
  during cleanup.
- Reporters are now **shared across assertions** rather than reconstructed
  per assertion (commit `4876603`).

When you change reporter wiring, keep `Reporters` keyed by their configured
type, preserve `Assertion.ReporterTypes` as the per-assertion routing gate, and verify with
`QaaS.Runner.Assertions.Tests`.

## YAML configuration shape

Top-level sections:
`MetaData`, `DataSources`, `Storages`, `Sessions`, `Assertions`, `Links`, `Variables`.

A `Session` contains `Publishers`, `Consumers`, `Transactions`, `Probes`,
`Collectors`, `MockerCommands`. Each Action carries `Name`, `Stage`,
`DataSourceNames`, `DataSourcePatterns`, `Parallel.Parallelism`, `Policies`,
plus protocol-specific config.

The canonical YAML reference lives at
<https://docs.qaas.online/qaas/userInterfaces/runner/configurationSections/configurationSections/>
(or `qaas-docs/docs/qaas/userInterfaces/runner/configurationSections/`).
**If you change the YAML schema, update the docs in lock-step.**

## Builder partial-class convention

Each builder is split into three (or more) partials in the same folder:

- `<Name>Properties.cs` — public fluent properties + `[Description]`,
  `[Required]`, `[DefaultValue]`, `[Range]`.
- `<Name>Logic.cs` — the fluent `.With…(…)` methods, `Build(...)`,
  internal helpers.
- `<Name>Validation.cs` — `[ValidatedRequiredIfAny]`, custom
  `ValidationAttribute`s, cross-property checks.

XML doc comments on every public member; include a `<qaas-docs>` tag pointing
at the corresponding docs URL when one exists. The docs generator
(`QaaS.Docs.Generator`) consumes those comments — do not break the format.

## Forbidden patterns (NEVER do)

1. `[Test(Ignore=…)]` / `[Fact(Skip=…)]` / commenting out a failing test —
   diagnose the root cause.
2. `try { … } catch { }` to silence a failure — at minimum log; preferably
   propagate or convert to a typed result.
3. `Result` / `.Wait()` / `GetAwaiter().GetResult()` on `async` paths.
4. Bypassing the hook-discovery system (e.g. `new MyGenerator()` inside
   `ExecutionBuilder`) — go through `IHookProvider` so users can swap in
   their own hooks.
5. Mutating `SessionData` after the owning Session completes.
6. Mutating an `ExecutionBuilder` after `Build()` has been called.
7. Holding action references across stages (each stage re-acquires).
8. Throwing `StopActionException` outside a `Parallel.ForEach` body.
9. Unbounded `Parallel.ForEach` — always pass a `ParallelOptions` with
   `MaxDegreeOfParallelism` derived from the configured `Parallel.Parallelism`.
10. Hard-coding reporter types — route via `ReporterType` in
    `ReportLogic`.
11. Caching hook instances across executions (each run rebuilds the scope).
12. Adding a new top-level YAML section without updating the YAML schema
    artifacts in `Documentation/` and the docs site.

## Must-verify before declaring done

1. `dotnet build QaaS.Runner.sln --nologo -clp:ErrorsOnly` → exit 0.
2. `dotnet test QaaS.Runner.sln --nologo --no-build` → all green.
3. `csharpier format` ran on touched files; no diff after formatting.
4. New/changed YAML attributes are reflected in builder partials *and* the
   docs site (or a follow-up doc PR is opened in `qaas-docs`).
5. New hook types are loadable via assembly scanning (cover with a test in
   the relevant `*.Tests` project).
6. `Parallel.ForEach` bodies do not capture mutable state without `lock` /
   `Interlocked` / a thread-safe collection.
7. New CLI options have help text and parse round-trip tests.
8. `RunnerAlphaSmokeApp` still smoke-runs (manual, when you change runtime
   wiring).
9. CI pipeline `.github/workflows/ci.yml` passes for the branch.
10. Coverage badges did not regress meaningfully (the gist endpoint visible
    on the README).

## Key files for orientation

- `QaaS.Runner/Bootstrap.cs` — CLI entry; constructs the Autofac container.
- `QaaS.Runner/ExecutionBuilder.cs` — central graph builder.
- `QaaS.Runner/Logics/SessionLogic.cs` — Stage orchestration.
- `QaaS.Runner/Logics/AssertionLogic.cs` — assertion driver.
- `QaaS.Runner/Logics/ReportLogic.cs` — reporter routing.
- `QaaS.Runner/Logics/StorageLogic.cs` — act/assert split.
- `QaaS.Runner.Sessions/Session/Session.cs` — session runtime.
- `QaaS.Runner.Sessions/Actions/Transactions/Transaction.cs` — parallelism
  reference implementation.
- `QaaS.Runner.Sessions/Extensions/IterableSerializableDataIterator.cs` —
  the data-iteration abstraction.
- `QaaS.Runner.Assertions/AllureReporter.cs` — reporter reference.
- `.github/workflows/ci.yml` — CI pipeline.

## Known recent / in-flight work

- PR #33 (`feature/transaction-parallelism`) — adds parallelism to
  transactions, bundles initial CLAUDE.md drop.
- PR #32 (`feature/reportportal_integration`) — passive ReportPortal
  reporter.
- PR #28 (`Skidskad:feat/clone-builders`) — `ICloneable<T>` deep-clone for
  builders (cross-repo with Mocker/Framework PR #31).

## When you are stuck

Read the docs site (live or the local `qaas-docs/` checkout). If the docs
contradict the code, fix the docs in `qaas-docs` *and* the code's XML doc
comments — the docs generator regenerates from source.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
