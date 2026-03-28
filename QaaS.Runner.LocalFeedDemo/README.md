# QaaS.Runner.LocalFeedDemo

Standalone local-feed smoke app for validating `QaaS.Runner` and `QaaS.Common.Generators`
package releases together.

The demo is intentionally kept outside `QaaS.Runner.sln` so the repo CI keeps validating the source
projects while this sample remains free to target local package tags.

## What It Checks

- restores `QaaS.Runner` from `D:\QaaS\LocalFeed\QaaS.Runner.4.2.1-alpha.1`
- restores `QaaS.Common.Generators` from `D:\QaaS\LocalFeed\QaaS.Common.Generators.3.2.1-alpha.2`
- runs a runner YAML that uses `FromFileSystem`
- configures only `GeneratorConfiguration:FileSystem:Path`
- verifies the generator defaults `DataArrangeOrder` to `Unordered`
- verifies the expected files are loaded without relying on ordering

## Run

```powershell
dotnet restore .\QaaS.Runner.LocalFeedDemo.csproj --configfile .\NuGet.config
dotnet run --project .\QaaS.Runner.LocalFeedDemo.csproj --configuration Release
```
