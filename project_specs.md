# project_specs.md — QaaS.Runner Solution

> Architectural specification for the **QaaS.Runner** repository.
> See `CLAUDE.md` for the AI-assistant operating manual, and the per-project
> `project_specs.md` files inside each `QaaS.Runner.*` directory.
> Live docs: <https://docs.qaas.online/>.

## 1. Purpose

`QaaS.Runner` is the test-orchestration engine of the QaaS platform. It loads
a YAML test definition, resolves all referenced hooks via
`QaaS.Framework.Providers`, builds a graph of *Sessions → Stages → Actions*,
executes the graph with deterministic concurrency, runs assertions over the
captured `SessionData`, and emits reports.

The runner is intentionally **protocol-agnostic and hook-agnostic**: it
delegates everything domain-specific to `QaaS.Framework` contracts and to
hook implementations discovered in user assemblies plus the QaaS Common
packages.

## 2. Scope and non-goals

In scope:

- CLI parsing (`run`, `act`, `assert`, `execute`, `template`).
- YAML loading + placeholder/reference resolution + DataAnnotations
  validation.
- Composition of the execution graph from builders.
- Concurrent execution with bounded parallelism per action.
- Assertion evaluation and report dispatch (Allure, ReportPortal).
- Session-data persistence (FileSystem, S3) for the `act → assert` split.

Out of scope:

- Concrete hook implementations (live in `QaaS.Common.*` and user code).
- Protocol drivers (live in `QaaS.Framework.Protocols`).
- Mock-server lifecycle (`QaaS.Mocker` owns that).

## 3. Solution structure

| Project | Role | Depends on |
|---|---|---|
| `QaaS.Runner` | CLI bootstrap, `ExecutionBuilder`, top-level logics, modules. | All siblings + `QaaS.Framework.{SDK,Configurations,Executions,Protocols,Providers,Serialization}` |
| `QaaS.Runner.Sessions` | Session/Stage/Action runtime, builders, iterators. | `Framework.{SDK,Protocols,Configurations,Policies}` |
| `QaaS.Runner.Assertions` | Assertion engine, reporters (`AllureReporter`, ReportPortal). | `Framework.SDK`, `Allure.NUnit`, `ReportPortal.*` |
| `QaaS.Runner.Storage` | FS / S3 session-data persistence. | `Framework.SDK`, `AWSSDK.S3` |
| `QaaS.Runner.Infrastructure` | Pure helpers (datetime, fs, templating). | None (leaf). |
| `QaaS.Runner.E2ETests` | Real-protocol round-trip tests. | All siblings + brokers. |
| `QaaS.Runner.*.Tests` | NUnit unit tests. | Their target project. |

Dependency graph is acyclic; `Infrastructure` is the leaf, `QaaS.Runner` is
the root.

## 4. Public surface

### 4.1 CLI commands

| Command | Purpose |
|---|---|
| `run` | Load, build, execute, assert, report — the standard end-to-end flow. |
| `act` | Execute sessions only; persist `SessionData` to a configured `Storage`. |
| `assert` | Reload persisted `SessionData` and run only assertions + reports. |
| `execute` | Lower-level entry exposing the raw `ExecutionBuilder` programmatically. |
| `template` | Scaffold a new test project from `QaaS.Runner.Template`. |

### 4.2 Programmatic ("Configuration as Code")

```csharp
var builder = new RunnerExecutionBuilder()
    .WithMetaData(m => m.WithTeam("team-x").WithProduct("p1"))
    .WithDataSources(new FixedDataDataSourceBuilder().Named("src").WithItems(...))
    .WithSession(s => s
        .Named("smoke")
        .WithPublisher(p => p
            .Named("send")
            .AddDataSource("src")
            .Configure(new RabbitMqSenderConfig { ... })
            .WithParallelism(8)));

int exitCode = builder.Build().Start();
```

All builders are partial classes:

- `*Properties.cs` — fluent properties + DataAnnotations.
- `*Logic.cs` — `.With…` setters, `.Build()`.
- `*Validation.cs` — cross-property validation attributes.

### 4.3 YAML

Top-level: `MetaData`, `Variables`, `Links`, `Storages`, `DataSources`,
`Sessions`, `Assertions`. Sub-shapes documented at
<https://docs.qaas.online/qaas/userInterfaces/runner/configurationSections/>.

## 5. Execution model

### 5.1 Composition

`ExecutionBuilder.Build()` walks the configuration in this fixed order:

1. `BuildDataSources()` — resolves `IGenerator` hooks for each data source
   builder, materialises a `DataSource` list.
2. `BuildSessions()` — for each session: resolves `IProbe` hooks; for each
   action: instantiates protocol senders/readers/transactors via the
   `QaaS.Framework.Protocols` factories.
3. `BuildAssertions()` — resolves `IAssertion` hooks, wires the configured
   reporter type.
4. `BuildStorages()` — instantiates persistence backends.
5. `BuildLinks()` — adds Allure/external link resolvers.

### 5.2 Run

- `Runner.Run()` (a.k.a. `RunAndGetExitCode()`) drives the execution.
- `SessionLogic` advances stage-by-stage; within a stage all Actions launch
  via `Task.Run` and are awaited together.
- Each Action iterates data via `IterableSerializableDataIterator`. With
  `Parallel.Parallelism > 1` the iterator uses
  `Parallel.ForEach` with a `SemaphoreSlim`-bounded body.
- Per-item flow inside an Action: get data → serialize → send/transact via
  protocol → deserialize response → log input/output (locked) → evaluate
  Policies (may throw `StopActionException` to short-circuit).
- Live action data is mirrored into `RunningCommunicationData<object>` on
  `InternalContext` for cross-action observation.
- `AssertionLogic` runs each `IAssertion` against the assembled
  `IImmutableList<SessionData>`, collecting `AssertionResult`s.
- `StorageLogic` (act/assert split) optionally persists/restores
  `SessionData` between phases.
- `ReportLogic` selects reporters by type (`Allure`, `ReportPortal`) and
  flushes attachments.
- Exit code: `0` pass, `1` assertion fail, `2` configuration / parse error.

### 5.3 Concurrency invariants

- `Parallel.ForEach` body **must** treat all captured collections as shared
  mutable state and synchronise.
- `StopActionException` is the legal early-exit sentinel — caught only by
  `IterableSerializableDataIterator.ApplyToAll`.
- `Interlocked.Increment` for atomic counters.
- `BlockingCollection<T>` for streaming RCD between actions.

## 6. Hook discovery

Provided by `QaaS.Framework.Providers`. Discovery order: entry assembly →
all loaded assemblies → `*.dll` in `BaseDirectory`. Sorted by priority
(`QaaS.*` = 0, `Common.*` = 1, others = 2) then by name. Hooks register as
`(string Name, IHook Instance)` pairs in the Autofac scope; the runner
resolves them by `IList<KeyValuePair<string, THook>>`.

Resolution by name:

- Exact `FullName` / `AssemblyQualifiedName` first (must be unique).
- Otherwise simple `Type.Name`, with priority groups breaking ties.

Configuration is loaded onto the resolved hook via
`hook.LoadAndValidateConfiguration(IConfiguration)` and validated with
DataAnnotations.

## 7. Reporting

- `IReporter` abstraction in `QaaS.Runner.Assertions`.
- `AllureReporter` writes Allure JSON + attachments. Attachments are
  deduplicated through a `ConcurrentDictionary<hash, path>`.
- `ReportPortalReporter` provides passive (post-run) reporting; selected
  by `ReporterType` in YAML.
- Recent change: reporters are **shared** across assertions rather than
  rebuilt per-assertion (see `06e23d1`, `4876603`, `9da3c76`).

## 8. Persistence (Storage)

`QaaS.Runner.Storage` exposes a `IStorage` abstraction with two concrete
implementations: `FileSystemStorage`, `S3Storage`. Used by:

- `act` writes `SessionData` to a configured storage (per-session JSON).
- `assert` reads them back so assertions can run elsewhere/later.

## 9. Build, packaging, CI

- Target: `.NET 10.0`, `nullable enable`, `TreatWarningsAsErrors=true`.
- Test runner: NUnit 4.x + Moq.
- Style: `csharpier` (run as part of pre-commit / CI).
- CI: `.github/workflows/ci.yml` — restore → build → test → coverage
  (`dotnet-coverage` + `reportgenerator`) → optional pack-and-push on stable
  tags. Concurrency group cancels superseded runs.
- NuGet identity: `QaaS.Runner` package; symbols embedded
  (`-p:DebugType=embedded`, `-p:EmbedAllSources=true`).
- Coverage badges live on a public Gist linked from the README.

## 10. Quality requirements

- Public builders: `[Description]` on every property, `[Required]` /
  `[Range]` / `[DefaultValue]` where appropriate, XML doc comments with
  `<summary>`, `<remarks>`, and `<qaas-docs>` link.
- Cross-property validation: bespoke `ValidationAttribute`s, not runtime
  `if/throw`.
- No silent failures: hooks return `List<ValidationResult>?` (null = no
  errors); the runner surfaces them in CLI output.
- Test new YAML attributes round-trip through serialiser + builder.

## 11. Compatibility & versioning

- Public API surface (builders, CLI, YAML) follows SemVer per stable git
  tag (`v{MAJOR}.{MINOR}.{PATCH}`).
- Breaking YAML changes require a docs update *and* a major-version bump.
- Internal types behind `internal` are not part of the public contract.

## 12. Roadmap signals

Tracked in open PRs:

- Transaction parallelism (PR #33) — landing.
- ReportPortal integration (PR #32) — adds passive reporter.
- Builder cloning (PR #28) — `ICloneable<T>` deep-clone of builders.

## 13. References

- Live docs: <https://docs.qaas.online/>
- Docs source: `qaas-docs` repo, `docs/qaas/userInterfaces/runner/`.
- Framework contracts: `QaaS.Framework` repo.
- Common hooks: `QaaS.Common.{Generators,Assertions,Probes}`.
- Smoke harness: `RunnerAlphaSmokeApp`.
