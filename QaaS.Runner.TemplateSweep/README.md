# QaaS.Runner.TemplateSweep

`QaaS.Runner.TemplateSweep` is a release-validation sample executable for Runner configuration templates.

It generates two valid template cases for every Runner-specific configuration context that the release exposes:

- `RequiredOnly`: leaves optional fields blank and keeps only fields that are required by validation.
- `Filled`: assigns explicit values to the same context while preserving cross-field invariants.

The executable writes generated cases and reports under the repository `artifacts` folder:

- `artifacts/cases`
- `artifacts/reports/runner-template-sweep-report.md`
- `artifacts/reports/runner-template-sweep-report.json`

Run it with:

```powershell
dotnet run --project D:\QaaS\QaaS.Runner\QaaS.Runner.TemplateSweep\QaaS.Runner.TemplateSweep.csproj -c Release
```

The process exits `0` only when every generated template case exits `0`.
