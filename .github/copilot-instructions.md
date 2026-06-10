Read `AGENTS.md` at the repo root first — it contains the CLI verbs, YAML test-plan format,
build commands, and the hook-loading and act/assert-split gotchas for this Tier-2 execution engine.

## Essentials
- **TFM**: net10.0; C# nullable + ImplicitUsings enabled.
- **Test framework**: NUnit 4.x + Moq + Allure.NUnit + ReportPortal.
- **Build**: `dotnet build -m QaaS.Runner.sln`.
- **Test**: `dotnet test QaaS.Runner.sln` — `QaaS.Runner.E2ETests` has `OutputType=Exe` and is not discoverable by `dotnet test`; skip it by excluding it from any standard test run.
- **Canonical run**: `dotnet run --project QaaS.Runner -- run test.qaas.yaml`.
- **Hook loading**: hooks are discovered via assembly scanning (entry + loaded assemblies + `*.dll` in app base). Assembly-name priority (`QaaS.*` → `Common.*` → others) breaks ties when multiple assemblies expose the same simple type name; `FullName`/`AssemblyQualifiedName` is preferred over `Type.Name`.
- **Act/Assert split**: `act` writes transaction logs to `Storages`; `assert` reads them back — `Storages` config must be identical across both invocations.
- **Commits**: conventional style (`feat:`, `fix:`); run `csharpier format` on changed files before committing.
