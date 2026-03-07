# ReportPortal Integration

## Summary
QaaS.Runner can now publish the runner's assertion results to ReportPortal in addition to the existing Allure output.

This integration is on the runner reporting path, not the `dotnet test` path:
- Allure reporting still writes to `allure-results` exactly as before.
- When ReportPortal is enabled, the same assertion results are also pushed to a single ReportPortal launch for the runner invocation.
- `-s` still only serves Allure locally. ReportPortal is viewed through its own UI.

## What Changed
- Added an optional top-level `ReportPortal` configuration section.
- Added environment variable support for the ReportPortal endpoint, project, API key, launch name, description, and debug mode.
- Added a `ReportPortalReporter` alongside `AllureReporter` in the existing `IReporter` fan-out.
- Added a runner-scoped `ReportPortalLaunchManager` so one runner invocation creates one ReportPortal launch.
- Updated runner teardown to finish the ReportPortal launch before optional Allure serving begins.
- Added `ReportPortal` to the configuration sections included in generated template attachments.
- Added a dedicated E2E config: [`QaaS.Runner.E2ETests/reportportal.e2e.qaas.yaml`](../QaaS.Runner.E2ETests/reportportal.e2e.qaas.yaml)

## Configuration
ReportPortal can be configured in YAML, environment variables, or a mix of both.

### YAML
```yaml
ReportPortal:
  Enabled: true
  Endpoint: http://localhost:8080
  Project: QaaS
  ApiKey: <reportportal-api-key>
  LaunchName: QaaS Runner Local
  Description: Local runner validation
  DebugMode: true
  Attributes:
    Environment: local
    Source: manual
```

### Environment Variables
- `QAAS_REPORTPORTAL_ENABLED`
- `QAAS_REPORTPORTAL_ENDPOINT`
- `QAAS_REPORTPORTAL_PROJECT`
- `QAAS_REPORTPORTAL_API_KEY`
- `QAAS_REPORTPORTAL_LAUNCH_NAME`
- `QAAS_REPORTPORTAL_DESCRIPTION`
- `QAAS_REPORTPORTAL_DEBUG_MODE`

Notes:
- `QAAS_REPORTPORTAL_ENDPOINT` can point either at the gateway URL, for example `http://localhost:8080`, or directly at the API URL. The runner normalizes gateway URLs to `/api/`.
- If `ReportPortal.Enabled` is true and the endpoint, project, or API key is missing after environment overrides are applied, the run fails fast.

## Reporting Model
When ReportPortal is enabled:
- One QaaS runner invocation creates one ReportPortal launch.
- Each reported QaaS assertion result becomes one ReportPortal `TEST` item.
- Item attributes include `tool=QaaS`, the assertion type, and the QaaS severity.
- Item parameters include the resolved session names and data source names.
- Assertion outcome details are written as logs.
- The execution configuration template is uploaded as a log attachment when `SaveTemplate` is enabled.
- Session data is uploaded as JSON log attachments when `SaveSessionData` is enabled.
- Custom assertion attachments are uploaded as log attachments when `SaveAttachments` is enabled.

Allure and ReportPortal are written from the same runner-generated `AssertionResult` objects.

## Local Validation
Validated on March 7, 2026 against a local ReportPortal deployment running on `http://localhost:8080`.

### Local ReportPortal Project
- Project: `QaaS`
- Created and reused in the local instance for validation

### Local API Key
- Created for local validation under the superadmin user
- Key name used during validation: `qaas-runner-local-20260307-190139`
- The key value is not stored in the repository

### E2E Command Used
```powershell
$env:QAAS_REPORTPORTAL_ENDPOINT = 'http://localhost:8080'
$env:QAAS_REPORTPORTAL_PROJECT = 'QaaS'
$env:QAAS_REPORTPORTAL_API_KEY = '<your-api-key>'
dotnet run --project QaaS.Runner.E2ETests\QaaS.Runner.E2ETests.csproj -- run QaaS.Runner.E2ETests\reportportal.e2e.qaas.yaml --send-logs false
```

### Observed Launch
- Launch UUID: `1d3b603a-ef8d-4c62-b0f7-3a402a019b1b`
- Launch name: `QaaS Runner E2E`
- Launch mode: `DEBUG`
- Launch status: `PASSED`

### Observed Items
- `ReportPortalPassedAssertion`
- `ReportPortalDataSourceAssertion`

Both items were present in the `QaaS` project, both were `PASSED`, and both contained:
- QaaS-specific attributes
- session/data-source parameters
- outcome logs
- template attachment logs

## Files
Main implementation files:
- [`QaaS.Runner.Assertions/ReportPortalReporter.cs`](../QaaS.Runner.Assertions/ReportPortalReporter.cs)
- [`QaaS.Runner.Assertions/ReportPortalLaunchManager.cs`](../QaaS.Runner.Assertions/ReportPortalLaunchManager.cs)
- [`QaaS.Runner.Assertions/ConfigurationObjects/ReportPortalConfig.cs`](../QaaS.Runner.Assertions/ConfigurationObjects/ReportPortalConfig.cs)
- [`QaaS.Runner.Assertions/ConfigurationObjects/AssertionBuilder.cs`](../QaaS.Runner.Assertions/ConfigurationObjects/AssertionBuilder.cs)
- [`QaaS.Runner/ExecutionBuilder.cs`](../QaaS.Runner/ExecutionBuilder.cs)
- [`QaaS.Runner/Runner.cs`](../QaaS.Runner/Runner.cs)

Validation and sample config:
- [`QaaS.Runner.E2ETests/reportportal.e2e.qaas.yaml`](../QaaS.Runner.E2ETests/reportportal.e2e.qaas.yaml)

## Operational Notes
- ReportPortal support is optional. If it is not enabled, runner behavior remains Allure-only.
- ReportPortal configuration must be consistent across all executions inside the same runner invocation.
- The launch is finalized during runner teardown before optional Allure serving starts.
