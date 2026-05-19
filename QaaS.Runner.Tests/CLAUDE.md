# CLAUDE.md — QaaS.Runner.Tests

## Purpose
Unit tests for the main `QaaS.Runner` project. Covers CLI bootstrap, execution builder, runner orchestration, loaders, and constants.

## Test Structure
- `BootstrapTests.cs` — CLI argument parsing and verb routing
- `BuilderTests/` — ExecutionBuilder YAML deserialization tests
- `RunnerTests/` — Runner orchestration flow tests
- `ExecutionTests/` — Execution context tests
- `LoadersTests/` — Configuration loader tests
- `LogicsTests/` — Domain logic tests (session, assertion, storage)
- `RunnerConstantsTests.cs` — Constants validation (reference paths, publisher/transaction data source paths)
- `TestData/` — YAML test fixtures
- `Globals.cs` — Shared test logger

## Running Tests
```bash
dotnet test QaaS.Runner.Tests/QaaS.Runner.Tests.csproj
```
