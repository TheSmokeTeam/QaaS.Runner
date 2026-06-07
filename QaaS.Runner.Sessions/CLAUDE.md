# CLAUDE.md — QaaS.Runner.Sessions

## Purpose
Session runtime engine. Contains all action types (publishers, consumers, transactions, probes, collectors, mocker commands), their builders, the stage-based execution model, and configuration objects.

## Architecture

### Action Hierarchy
```
Action (abstract) — base: Name, Act(), Dispose()
  └─ StagedAction (abstract) — adds: Stage, Policies, ExportRunningCommunicationData(), LogData()
       ├─ BasePublisher (abstract) — adds: Parallelism, SemaphoreSlim, ApplyToAll
       │    ├─ Publisher — single-message send via ISender
       │    └─ ChunkPublisher — batch send via IChunkSender
       ├─ Transaction — request/response via ITransactor, supports Parallelism
       ├─ BaseConsumer (abstract)
       │    ├─ Consumer — single-message read via IReader
       │    └─ ChunkConsumer — batch read via IChunkReader
       ├─ Probe — hook execution via IProbe
       └─ Collector — data fetching via IFetcher
```

### Session Execution Flow
1. `SessionBuilder.Build()` creates all actions and groups them into `Stage` objects by stage number
2. `Session.RunAsync()` iterates stages in order; within each stage, all actions run concurrently
3. Each action's `Act()` method processes data and returns `InternalCommunicationData<object>`
4. Results are aggregated into `SessionData` with inputs/outputs

### Parallelism Pattern (Publishers & Transactions)
- Builder exposes `Parallel` property (`ConfigurationObjects.Parallel` record with `Parallelism` int)
- Runtime action stores `int? _parallelism` and `SemaphoreSlim? _parallelismSemaphore`
- Data processing uses `IterableSerializableDataIterator.ApplyToAll(data, callback, parallel: true)`
- `ApplyToAll` with `parallel: true` calls `Parallel.ForEach`; with `false` runs sequential `foreach`
- `LogData` uses `lock` on shared `actData.Input`/`actData.Output` lists for thread safety
- `StopActionException` breaks out of `Parallel.ForEach` when policy chain returns false

## Key Directories
- `Actions/Publishers/` — BasePublisher, Publisher, ChunkPublisher + Builders/
- `Actions/Transactions/` — Transaction + Builders/
- `Actions/Consumers/` — BaseConsumer, Consumer, ChunkConsumer + Builders/
- `Actions/Probes/` — Probe + Builder
- `Actions/Collectors/` — Collector + Builder
- `Actions/MockerCommands/` — MockerCommand + Builder
- `Session/` — Session, ISession, Stage + Builders/
- `ConfigurationObjects/` — Parallel, Chunks, OrderedActions, etc.
- `Extensions/` — IterableSerializableDataIterator, SessionExtensions
- `RuntimeOverrides/` — Test hook overrides for action factories

## Configuration Objects
- `Parallel` — `{ Parallelism: int }` — controls concurrent execution count
- `Chunks` — `{ ChunkSize: int }` — controls batch size for chunk publishers
- `OrderedActions` — enum defining default stage order: Consumers=0, Publishers=1, Transactions=2, Probes=3, MockerCommands=4

## Builder Conventions
- Partial classes split across `*Properties.cs` (YAML-bindable properties), `*Logic.cs` (fluent API + Build), `*Validation.cs` (IValidatableObject)
- All fluent methods return `this` for chaining
- `Build()` is `internal`, accepts `InternalContext`, `IList<ActionFailure>`, session name
- Build failures are caught and appended to `actionFailures` (never thrown)

## Build
```bash
dotnet build QaaS.Runner.Sessions/QaaS.Runner.Sessions.csproj
```
