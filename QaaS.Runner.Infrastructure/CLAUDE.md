# CLAUDE.md — QaaS.Runner.Infrastructure

## Purpose
Small shared cross-project utility library. No domain logic — only reusable helpers consumed by other projects.

## Key Files
- `ConfigurationTemplateRenderer.cs` — Handlebars-style template rendering for YAML configuration values
- `DateTimeExtensions.cs` — Date/time parsing, formatting, and timezone-aware conversions
- `TimeZoneInfoResolver.cs` — Resolves timezone IDs with fallback (`Israel` default)
- `FileSystemExtensions.cs` — File/directory helpers (path normalization, safe reads)
- `ContextArtifactExtensions.cs` — Extensions for attaching artifacts to the execution context
- `ContextMetadataExtensions.cs` — Extensions for reading/writing metadata in context
- `Constants.cs` — Shared infrastructure constants

## Build
```bash
dotnet build QaaS.Runner.Infrastructure/QaaS.Runner.Infrastructure.csproj
```
