# ReportPortal Integration PR

## Summary
This PR adds optional ReportPortal publishing for runner-produced QaaS assertion results while keeping the existing Allure reporting flow intact.

## Scope
- Added an optional `ReportPortal` YAML section plus environment variable overrides.
- Added a `ReportPortalReporter` that writes the same `AssertionResult` objects already used by Allure.
- Added a runner-scoped `ReportPortalLaunchManager` so each runner invocation maps to one ReportPortal launch.
- Finalized ReportPortal launches during runner teardown before optional Allure serving.
- Added an E2E config for local validation.
- Documented setup, behavior, and validation in [`docs/reportportal-integration.md`](./reportportal-integration.md).

## Validation
- Built:
  - `QaaS.Runner.Assertions`
  - `QaaS.Runner`
  - `QaaS.Runner.E2ETests`
- Ran the E2E executable through the runner, not `dotnet test`.
- Validated against local ReportPortal project `QaaS`.
- Observed launch `1d3b603a-ef8d-4c62-b0f7-3a402a019b1b` in `DEBUG` mode with status `PASSED`.
- Observed the following ReportPortal items:
  - `ReportPortalPassedAssertion`
  - `ReportPortalDataSourceAssertion`

## Notes
- Allure output is unchanged and still writes to `allure-results`.
- ReportPortal is optional and only activates when configured.
