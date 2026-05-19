# CLAUDE.md — QaaS.Runner.Storage

## Purpose
Storage abstraction for persisting and retrieving serialized session data between act and assert phases.

## Key Files
- `IStorage.cs` — Storage interface
- `BaseStorage.cs` — Abstract base with shared serialization/deserialization logic
- `FileSystemStorage.cs` — Local filesystem storage implementation
- `S3Storage.cs` — S3-compatible object storage implementation
- `StorageBuilder.cs` — Builder for constructing storage instances from YAML config
- `CaseStorageHandler.cs` — Case-level storage path management
- `ConfigurationObjects/` — Storage configuration models

## Build
```bash
dotnet build QaaS.Runner.Storage/QaaS.Runner.Storage.csproj
```
