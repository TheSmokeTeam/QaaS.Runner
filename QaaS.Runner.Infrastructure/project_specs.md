# project_specs.md — QaaS.Runner.Infrastructure

> Per-project specification. For the solution-level spec see
> `../project_specs.md`. For the AI operating manual see `../CLAUDE.md`.

## Role

Pure utility leaf project — zero behavioural state, no external services.
Used by every other project in the solution.

## Key files

| File | Purpose |
|---|---|
| `ConfigurationTemplateRenderer.cs` | Renders inline templates (variables, references) inside configuration values. |
| `ContextArtifactExtensions.cs` | Helpers for attaching artifacts to the runtime `Context`. |
| `ContextMetadataExtensions.cs` | Helpers for accessing structured metadata on `Context`. |
| `DateTimeExtensions.cs` | Timezone-aware conversion helpers (delegates to `TimeZoneInfoResolver`). |
| `FileSystemExtensions.cs` | Path-safety helpers (sanitise directory names cross-platform). |
| `TimeZoneInfoResolver.cs` | Wrapper around `TimeZoneInfo.FindSystemTimeZoneById`. |
| `Constants.cs` | Shared string constants. |

## Conventions

- Everything is `static`. No DI, no config, no I/O hidden in surprising
  places.
- Cross-platform: code must work on Windows and Linux CI. No
  hard-coded path separators — use `Path.Combine`.

## Forbidden in this project

- Adding a transitive dependency on any other `QaaS.Runner.*` project
  (this project sits *below* them all).
- Adding I/O that isn't strictly utility-scoped (e.g. no HTTP calls).

## Tests

`QaaS.Runner.Infrastructure.Tests` — covers each helper edge case.
