# QaaS.Runner

The `QaaS.Runner` is a package available as part of the `QaaS` Framework that is
used for running backend and e2e testing projects.

> Written In C# 14 & .net 10

## Projects

### User Interfaces

* `QaaS.Runner` - From here the QaaS run is initialized, can be treated as the QaaS entrypoint that acts like a CLI
 and should receive the command line arguments given to the dotnet run.
* `QaaS.Runner.SchemaGenerator` - A project that generates a `json schema` for `.qaas.yaml` configuration files, used in CI to
 automatically generate the schema as an artifact.

### Functional Projects

* `QaaS.Runner.Storage` - Responsible for managing the storage of QaaS Objects.

* `QaaS.Runner.Sessions` - Responsible for running sessions according to given configuration.

* `QaaS.Runner.Assertions` - Responsible for building, running and writing the results of given assertions according to given configuration.

### Packages

* `QaaS.Runner.Infrastructure` - Any common functionality used across different QaaS projects that is too small to have a project of its own.

### Tests

* `QaaS.Runner.IntegrationTests` - Integration test project for all QaaS projects with any outside source.

* `QaaS.Runner.<OtherQaaSRunnerProjectName>.Tests` - NUnit test project for the other project referenced in the name.
