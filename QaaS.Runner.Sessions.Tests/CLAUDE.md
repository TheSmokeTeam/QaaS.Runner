# CLAUDE.md — QaaS.Runner.Sessions.Tests

## Purpose
Unit tests for `QaaS.Runner.Sessions`. Uses NUnit 4.x + Moq. Tests cover all action types, builders, session execution, and extensions.

## Test Structure
- `Actions/Publisher/` — PublisherTest (includes parallelism tests), PublisherBuilderTests
- `Actions/Transactions/` — TransactionTests, TransactionBuilderTests
- `Actions/Utils/` — CreationalFunctions (shared test factory methods), TestResourceDataSources
- `Session/` — SessionTests, StageTests, SessionBuilderTests, SessionBuilderCrudTests, SessionExecutionCoverageTests
- `Extensions/` — SessionExtensionsTests, IterableSerializableDataIterator tests
- `RuntimeOverrides/` — Override mechanism tests
- `Globals.cs` — Shared test logger instance

## Key Patterns
- **CreationalFunctions** — Static factory methods for creating mock-backed actions (publishers, consumers, transactions)
- **TestResourceDataSources** — NUnit `TestCaseSource` data provider with various DataSource configurations
- **Reflection-based testing** — Tests invoke `internal`/`protected` methods via reflection (e.g., `Publish`, `Transact`)
- **Parallelism verification** — Uses `Interlocked` counters + `Thread.Sleep` to measure max concurrent threads

## Running Tests
```bash
dotnet test QaaS.Runner.Sessions.Tests/QaaS.Runner.Sessions.Tests.csproj
dotnet test QaaS.Runner.Sessions.Tests --filter "TransactionTests"
dotnet test QaaS.Runner.Sessions.Tests --filter "PublisherTest"
```
