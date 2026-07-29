# AGENTS.md — QaaS.Runner
Guidance for AI agents working in this repository.

## What this repo is
QaaS.Runner is the test execution and orchestration engine (Tier 2). It loads YAML test plans
(`test.qaas.yaml`), resolves hooks via QaaS.Framework.Providers assembly scanning, builds a
Sessions → Stages → Actions execution graph with bounded parallelism, evaluates assertions
over transaction logs, and reports via Allure and ReportPortal. Target: net10.0.

## Projects / Layout
| Project | Purpose |
|---|---|
| QaaS.Runner | CLI bootstrap (`Bootstrap.cs`, CommandLineParser), Autofac container, YAML deserialization, `ExecutionBuilder.cs` composition root |
| QaaS.Runner.Sessions | Actions runtime: Publisher, Consumer, Transaction, Probe, Collector, MockerCommand; `IterableSerializableDataIterator` drives parallel iteration |
| QaaS.Runner.Assertions | Assertion evaluation, `AllureReporter`, `ReportPortalReporter`, `LinkBuilder` (Kibana/Prometheus/Grafana) |
| QaaS.Runner.Storage | Persistence: FileSystem and Amazon S3 (enables Act/Assert split) |
| QaaS.Runner.Infrastructure | Template rendering, datetime math, timezones |
| QaaS.Runner.E2ETests | Integration tests with real brokers (RabbitMQ) |

## Build & test
```shell
dotnet build -m QaaS.Runner.sln
dotnet test QaaS.Runner.sln
# Note: QaaS.Runner.E2ETests has OutputType=Exe and is NOT run by dotnet test.
# Canonical run:
dotnet run --project QaaS.Runner -- run test.qaas.yaml
```

## CLI verbs
| Verb | Purpose |
|---|---|
| `run` | Full flow: load → act → assert → report |
| `act` | Execute sessions, persist transaction data to Storage (no assertions) |
| `assert` | Restore logs from Storage, run assertions only |
| `template` | Merge configs, resolve `${...}` placeholders, output resolved YAML |
| `execute` | Sequential batch of QaaS commands from an execution-sequence YAML |

## YAML test format (top-level sections)
- **MetaData** — tags (Team, System, …)
- **Variables** — interpolated as `${variables:myVar}`
- **Storages** — `FileSystem: { Path: ... }` | S3
- **DataSources** — generator binding: `Generator: HookName`, `Count: N`
- **Sessions** — workflows containing Probes, Publishers (RabbitMq/Kafka), Consumers, Transactions (HTTP/gRPC)
- **Assertions** — `Assertion: <HookName>`, `SessionNames: [...]`
- **Links** — Kibana, Prometheus, Grafana dashboard links

## Critical gotchas
- **Depends on Tier-0 QaaS.Framework** — any SDK interface change (IGenerator, IAssertion, IProbe)
  requires updating Runner callsites; always align package versions.
- **Hook discovery**: hooks must be in assemblies discoverable by Providers scanning (entry assembly + loaded assemblies + `*.dll` in the app base directory). Assembly-name priority (`QaaS.*` → `Common.*` → others) is used as a tie-breaker for hooks sharing the same simple type name; resolution prefers `FullName`/`AssemblyQualifiedName` matches before falling back to `Type.Name`.
- **Act/Assert split** requires a shared `Storages` section in the YAML; the FileSystem path must
  be stable and identical across both runs.
- **`IterableSerializableDataIterator`** drives parallel Transaction execution (PR #33) — data
  iteration count and Transaction count must align; mismatches cause data-loss silently.
- **ReportPortal reporter is passive** alongside Allure raw JSON (PR #32) — results go to both;
  ReportPortal failure does not block test outcome.
- **`${variables:x}` placeholders** are resolved by QaaS.Framework.Configurations; do not
  pre-expand them in C# code.
- **E2ETests require live RabbitMQ** — always filter them out in unit/integration CI jobs.

## Process
Follow the QaaS harness pipeline: plan → contract → implement → adversarial evaluation
(rubric: Correctness/Completeness/Craft/Robustness, each ≥7/10). Write tests first (TDD).
Conventional commits: `feat:`, `fix:`, `chore(release):`.
Run `csharpier format` on changed files before committing.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
