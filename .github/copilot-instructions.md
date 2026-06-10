Read `AGENTS.md` at the repo root first — it contains the CLI verbs, YAML test-plan format,
build commands, and the hook-loading and act/assert-split gotchas for this Tier-2 execution engine.

## Essentials
- **TFM**: net10.0; C# nullable + ImplicitUsings enabled.
- **Test framework**: NUnit 4.x + Moq + Allure.NUnit + ReportPortal.
- **Build**: `dotnet build -m QaaS.Runner.sln`.
- **Test**: `dotnet test QaaS.Runner.sln` — use `--filter "FullyQualifiedName!~E2ETests"` to
  skip RabbitMQ-dependent tests in CI.
- **Canonical run**: `dotnet run --project QaaS.Runner -- run test.qaas.yaml`.
- **Hook loading**: hooks must be in assemblies matching `QaaS.*`, `Common.*`, or user libs;
  namespace matters for discovery order.
- **Act/Assert split**: `act` writes transaction logs to Storage; `assert` reads them back —
  Storage config must be identical across both invocations.
- **Commits**: conventional style (`feat:`, `fix:`); run `dotnet format` before committing.
