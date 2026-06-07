# project_specs.md — QaaS.Runner.Storage

> Per-project specification. For the solution-level spec see
> `../project_specs.md`. For the AI operating manual see `../CLAUDE.md`.

## Role

Pluggable persistence for `SessionData` between the `act` and `assert`
phases of the CLI. Keeps the runner free of any direct filesystem / S3
coupling.

## Key types

- `IStorage` — abstraction (`Save(session, data, ct)` / `Load(session, ct)`).
- `FileSystemStorage` — local-disk JSON serialisation, one file per session.
- `S3Storage` — AWS S3 backend using `AWSSDK.S3`. Bucket/prefix configured
  via the YAML `Storages` section.
- `ConfigurationObjects/*` — config records bound from YAML.

## YAML schema (excerpt)

```yaml
Storages:
  - Name: act-output
    Type: FileSystem
    BasePath: ./qaas-out
  - Name: shared
    Type: S3
    Bucket: qaas-runs
    KeyPrefix: nightly/{date}
```

## Conventions

- Serialisation goes through `QaaS.Framework.Serialization` factories
  (default JSON) — do not introduce a private serialiser.
- Errors surface as typed exceptions (`StorageReadException`,
  `StorageWriteException`) so the runner can produce a clear CLI message.

## Forbidden in this project

- Reading/writing files outside the configured `BasePath` / S3 prefix.
- Unbounded retry loops — fail fast and let the caller decide.

## Tests

`QaaS.Runner.Storage.Tests` — round-trip tests for both backends. S3 tests
use mocked `IAmazonS3` clients.
