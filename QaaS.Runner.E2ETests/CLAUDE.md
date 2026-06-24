# CLAUDE.md — QaaS.Runner.E2ETests

## Purpose
End-to-end integration tests that run full QaaS workflows against real infrastructure (RabbitMQ, etc.).

## Key Files
- `test.qaas.yaml` — Main E2E test configuration
- `executable.yaml` — Execution configuration
- `Generators/` — Custom test data generators
- `Assertions/` — Custom E2E assertion hooks
- `Probes/` — Custom E2E probe hooks
- `Variables/` — External variable files for YAML template rendering
- `Program.cs` — Test entrypoint

## Running
```bash
dotnet test QaaS.Runner.E2ETests/QaaS.Runner.E2ETests.csproj
```
