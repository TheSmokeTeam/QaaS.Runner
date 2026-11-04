# project_specs.md — QaaS.Runner.Sessions

> Per-project specification. For the solution-level spec see
> `../project_specs.md`. For the AI operating manual see `../CLAUDE.md`.

## Role

The **session runtime**. Houses every action type, their builders, the
data-iteration abstraction, and the Stage/Session executors. This is where
the runner spends >95% of its wall time during a real test run.

## Folder layout

```
Actions/
  Publishers/            — fire-and-forget protocol publishes
  Consumers/             — long-lived listeners (Kafka, RabbitMQ, …)
  Transactions/          — request-response (HTTP, gRPC, …)
  Probes/                — lifecycle hooks (setup/teardown)
  Collectors/            — historical collectors (Prometheus, Elastic, …)
  MockerCommands/        — out-of-band commands sent to a paired mocker
Builders/                — fluent builders (partials per action)
ConfigurationObjects/    — YAML-bound config records
Extensions/              — IterableSerializableDataIterator, helpers
Session/                 — Session.cs runtime + Stage logic
Policies/                — runtime wrappers around Framework policies
```

## Key types

| Type | Purpose |
|---|---|
| `Session` | Launches internal stages in order, awaits all staged action tasks, then runs collectors. |
| `Stage` | Launches its actions concurrently; before/after delays control launches without waiting for action completion. |
| `Action` (abstract) | Base for `Publisher` / `Consumer` / `Transaction` / `Probe` / `Collector` / `MockerCommand`. |
| `IterableSerializableDataIterator` | Wraps generator output and exposes `IterateEnumerable`, `IterateWithOriginal`, and bounded `ApplyToAll` overloads. |
| `RunningCommunicationData<T>` | Thread-safe live mirror of action I/O exposed on `InternalContext`. |
| `*Builder` partials | `*Properties` / `*Logic` / `*Validation`. |

## Action lifecycle (Transaction example)

1. Builder validated and `.Build()` invoked → produces `Transaction`.
2. Stage `Task.Run`s all actions; `Transaction` resolves data sources to a
   single iterable.
3. `ApplyToAll` executes sequentially when `Parallel` is omitted; a configured
   degree sets `ParallelOptions.MaxDegreeOfParallelism`, including degree one.
   Parallel transaction input is materialized before workers start.
4. For each item: serialise → call protocol `ITransactor.Transact()` →
   deserialise reply → append to `SessionData.Inputs/Outputs` under a
   `lock` → evaluate `Policy` chain.
5. On `StopActionException` the iterator exits cleanly (the chain reached a
   stop condition such as `CountPolicy` or `TimeoutPolicy`).

Publishers and transactions support per-action workers through
`Parallel.Parallelism`; chunk publishers count chunks as worker operations.
Consumers, collectors, probes and mocker commands have no per-action worker
setting. Separate consumer actions have separate protocol instances. Probes
execute their hook once; collectors run after all staged actions complete.

## Policies

Publisher, consumer and transaction builders accept a `Policies` list; runtime wraps them in the
`QaaS.Framework.Policies` chain. The chain is constructed in ascending
`Index` order; `RunChain()` returns `false` when a `*StopException` was
caught.

## Concurrency invariants

- Publisher and transaction loops bound `Parallel.ForEach` workers to the configured degree, with a nonbuffering partitioner and a send/transact semaphore.
- All shared collections under `lock`.
- `Interlocked` for counters.
- Parallel iteration unwraps aggregates containing only `StopActionException`; mixed transport failures propagate.
- Policies are evaluated under a per-action lock; already-running work can finish after a stop condition.

## Tests

`QaaS.Runner.Sessions.Tests` (NUnit + Moq) — covers builder partials,
iterator concurrency, action lifecycle, policy chain interactions.

## Forbidden in this project

- Direct protocol use — always go through `QaaS.Framework.Protocols`
  factories.
- Any `Thread.Sleep` / `Task.Delay` inside parallel bodies — use Policies.
- Capturing mutable state in `Parallel.ForEach` without synchronisation.
- Adding a new action type without a builder partial-trio and tests.
