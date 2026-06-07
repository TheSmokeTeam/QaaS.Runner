# CLAUDE.md — QaaS.Runner.Assertions

## Purpose
Assertion engine that evaluates session outputs against configured assertion hooks. Produces Allure-compatible test reports.

## Key Files
- `AllureReporter.cs` — Main reporter: builds Allure test results with steps, attachments, and links
- `BaseReporter.cs` — Abstract base for assertion reporters
- `IReporter.cs` — Reporter interface
- `AssertionObjects/` — Runtime assertion models
- `ConfigurationObjects/` — YAML-bindable assertion builder and configuration
- `LinkBuilders/` — Allure link generation helpers

## Build
```bash
dotnet build QaaS.Runner.Assertions/QaaS.Runner.Assertions.csproj
```
