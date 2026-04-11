# ReportPortal Integration Progress

This document describes the work currently applied on this branch for adding ReportPortal reporting alongside the existing Allure reporting. It is intended to help the next developer understand what was brought in from `codex/reportportal-integration`, what was adjusted locally to keep this branch working, how the feature currently flows, and what still needs attention.

## Current State

- The ReportPortal integration changes are applied and commited on the branch named `feature/reportportal_integration`.
- The implementation is passive: ReportPortal publishing should not replace Allure and should not fail a QaaS run when ReportPortal is unavailable.
- The current solution builds and the full test suite was verified after the merge work.

## What Was Taken From `codex/reportportal-integration`

The feature branch contributed a passive ReportPortal reporting path, tests, E2E fixtures, and documentation updates. The following work was taken from that branch and applied here.

### New ReportPortal Runtime Files

| File | Purpose |
|---|---|
| `QaaS.Runner.Assertions/ConfigurationObjects/ReportPortalConfig.cs` | Defines ReportPortal configuration and runtime-resolved settings. |
| `QaaS.Runner.Assertions/ReportPortalAccessValidator.cs` | Validates endpoint, API key, and team-project access without provisioning anything. |
| `QaaS.Runner.Assertions/ReportPortalLaunchManager.cs` | Owns ReportPortal launch lifecycle for one runner invocation. |
| `QaaS.Runner.Assertions/ReportPortalReporter.cs` | Publishes assertion results, logs, and attachments into ReportPortal. |
| `QaaS.Runner.Assertions/ReportPortalRunDescriptor.cs` | Builds stable runner-scoped launch identity, name, description, and attributes. |

### New Tests

| File | Purpose |
|---|---|
| `QaaS.Runner.Assertions.Tests/ConfigurationObjectsTests/ReportPortalConfigTests.cs` | Covers environment-variable resolution, endpoint normalization, team routing, and launch attributes. |
| `QaaS.Runner.Assertions.Tests/ReportPortalAccessValidatorTests.cs` | Covers passive access checks, missing key/team/project cases, unauthorized keys, and cache behavior. |
| `QaaS.Runner.Assertions.Tests/ReportPortalRunDescriptorTests.cs` | Covers stable launch naming and launch description content. |
| `QaaS.Runner.Assertions.Tests/BaseReporterTests.cs` | Adds tests for shared reporter helper behavior used by ReportPortal. |
| `QaaS.Runner.Tests/RunnerTests/RunnerBehaviorTests.cs` | Adds tests for ReportPortal launch manager injection and run descriptor grouping. |

### New E2E Assets

| Path | Purpose |
|---|---|
| `QaaS.Runner.E2ETests/Assertions/ReportPortal/` | Adds rich test assertions that produce deterministic messages, traces, and attachments for ReportPortal E2E checks. |
| `QaaS.Runner.E2ETests/Generators/ReportPortal/` | Adds deterministic payload generation for ReportPortal E2E scenarios. |
| `QaaS.Runner.E2ETests/Probes/ReportPortal/` | Adds diagnostic probes used to create session evidence and failure paths. |
| `QaaS.Runner.E2ETests/Configs/ReportPortal/` | Adds team/system ReportPortal scenarios and warning-path scenarios. |
| `QaaS.Runner.E2ETests/executable.yaml` | Adds commands for the new ReportPortal E2E scenarios. |
| `QaaS.Runner.E2ETests/QaaS.Runner.E2ETests.csproj` | Ensures new E2E config files are copied to output. |

### Project And Documentation Updates

| File | Change |
|---|---|
| `QaaS.Runner.Assertions/QaaS.Runner.Assertions.csproj` | Adds `ReportPortal.Client` dependency. |
| `README.md` | Adds ReportPortal usage and E2E documentation from the integration branch. |

## Adjustments Made On This Branch

The integration branch was not copied blindly in the final result. Several files in the current branch had newer behavior than `codex/reportportal-integration`. A direct overwrite broke build or tests, so the final local state keeps the current branch behavior and layers ReportPortal into it.

### `QaaS.Runner/Runner.cs`

The raw integration-branch version of `Runner.cs` removed current-branch lifecycle APIs and behavior. To fix this the file was restored to the current main branch implementation and then ReportPortal logic was added back:

- `Teardown()` now finishes any registered `ReportPortalLaunchManager` before normal teardown continues.
- `BuildExecutions()` now resolves a shared `ReportPortalLaunchManager` from the runner scope when registered.
- `BuildExecutions()` now builds grouped `ReportPortalRunDescriptor` instances and injects them into matching execution builders.
- Helper methods were added for grouped launch descriptor creation:
  - `BuildReportPortalRunDescriptors()`
  - `BuildSingleBuilderRunDescriptor()`
  - `BuildLaunchAttributes()`

Why this matters: the runner remains compatible with the current branch's CLI/bootstrap behavior, while ReportPortal still receives runner-scoped launch grouping.

### `QaaS.Runner/ExecutionBuilder.cs`

The raw integration-branch version of `ExecutionBuilder.cs` replaced current-branch behavior around generator resolution, validation, rendered template storage, and global dictionary merging. That caused existing tests to fail. The file was restored to the main branch implementation and only the ReportPortal-specific surface was added:

- Added optional `ReportPortalConfig? ReportPortal`.
- Added `_reportPortalLaunchManager`.
- Added `_reportPortalRunDescriptor`.
- Bound `ReportPortal` from context-loaded config.
- Updated `BuildReports()` to resolve `ReportPortalSettings` and pass them into `AssertionBuilder.BuildReporters(...)`.
- Added internal runner-injection/read helpers:
  - `WithReportPortalLaunchManager(...)`
  - `WithReportPortalRunDescriptor(...)`
  - `ReadExecutionType()`
  - `ReadCase()`
  - `ReadExecutionId()`

Why this matters: existing execution builder behavior remains intact, but ReportPortal can be configured and routed when the builder is used from the runner.

### `QaaS.Runner.Assertions/AllureReporter.cs`

The raw integration-branch version changed Allure attachment behavior and caused current-branch Allure tests to fail. The file was restored to the main branch implementation.

Why this matters: the user experience of the Allure report should remain unchanged. The ReportPortal feature is added alongside Allure, not by modifying how Allure writes output.

### `QaaS.Runner.Assertions/ConfigurationObjects/AssertionBuilder.cs`

`AssertionBuilder` now supports multi-reporter construction:

- `Build(...)` still creates the Allure reporter.
- `BuildReporters(...)` now returns Allure always.
- `BuildReporters(...)` also returns `ReportPortalReporter` when ReportPortal settings are enabled and a runner-scoped launch manager is available.
- Allure reporter naming was restored to the main branch behavior, meaning Allure reporter names are not suffixed with `(Allure)`.
- `SaveLogs` continues flowing into Allure and is available to ReportPortal.

Important local adjustment: if ReportPortal is enabled but no `ReportPortalLaunchManager` is provided, ReportPortal is skipped rather than throwing. This keeps direct `ExecutionBuilder.Build()` use cases and existing tests working. The normal runner path injects the manager.

### `QaaS.Runner.Assertions/BaseReporter.cs`

Some reporter-neutral helper methods were introduced or kept from the integration branch, and `SaveLogs` remains part of the shared reporter contract.

The base reporter now contains common data preparation that both reporters can use:

- `BuildAssertionArtifacts(...)`
- `BuildTemplateArtifact()`
- `BuildSessionArtifact(...)`
- `BuildMetadataAttributes()`
- `BuildAssertionContextArtifact(...)`
- `BuildStableReportPortalIdentity(...)`
- `BuildAssertionTextDetails(...)`
- `BuildSessionSummaryText(...)`
- `BuildActionFailureText(...)`
- `BuildFlakinessText(...)`
- `ExtractMetadataAttributes(...)`
- `GetAttachmentTypeBySerializationType(...)`

Why this matters: ReportPortal needs much of the same data that Allure already writes. Moving reporter-neutral shaping into `BaseReporter` avoids duplicating serialization, metadata flattening, text formatting, and stable identity logic.

## ReportPortal Feature Design

### Intent

The feature adds a second reporting sink. QaaS still generates Allure results, and the same assertion results can also be sent to ReportPortal.

The design is passive:

- Do not create ReportPortal projects.
- Do not create ReportPortal users.
- Do not create dashboards or filters.
- Do not let ReportPortal outages or auth errors fail the QaaS run.
- Warn and continue when ReportPortal publishing cannot happen.

### Routing Model

ReportPortal routing is based on QaaS metadata:

- `MetaData.Team` maps to the ReportPortal project name.
- `MetaData.System` is used for launch grouping inside that team project.
- `ReportPortal.Project` and `QAAS_REPORTPORTAL_PROJECT` are treated as ignored overrides. The code warns and continues to route by team.

This keeps ownership stable: each team owns its ReportPortal project, and systems are grouped under that project.

### Configuration Sources

Runtime-sensitive values are resolved through environment variables:

| Setting | Source |
|---|---|
| Enabled | `QAAS_REPORTPORTAL_ENABLED` or config fallback |
| Endpoint | `QAAS_REPORTPORTAL_ENDPOINT` |
| API key | `QAAS_REPORTPORTAL_API_KEY` |
| Ignored project override | `QAAS_REPORTPORTAL_PROJECT` or config `Project` |
| Launch name | config `LaunchName` or descriptor default |
| Description | config `Description` or descriptor default |
| Debug mode | config `DebugMode` |
| Attributes | descriptor attributes plus config `Attributes` |

The endpoint is normalized to a ReportPortal API URL. For example, a gateway URL is normalized toward `/api/`.

## Runtime Flow

1. Configuration is loaded into `ExecutionBuilder`.
2. `ExecutionBuilder` stores optional `ReportPortal` configuration.
3. `Runner.BuildExecutions()` groups execution builders by resolved team and system.
4. `Runner.BuildExecutions()` creates a `ReportPortalRunDescriptor` for each team/system group.
5. `Runner.BuildExecutions()` injects a shared `ReportPortalLaunchManager` and the matching run descriptor into each builder.
6. `ExecutionBuilder.BuildReports()` resolves `ReportPortalSettings` from `ReportPortalConfig` and the run descriptor.
7. `AssertionBuilder.BuildReporters()` builds the Allure reporter and when possible, also the ReportPortal reporter.
8. During reporting, `ReportLogic` fans assertion results out to the configured reporters.
9. `ReportPortalReporter.WriteTestResults(...)` asks the launch manager to start or reuse the shared launch.
10. `ReportPortalReporter` starts a ReportPortal test item, writes logs and attachments, then finishes the item.
11. `Runner.Teardown()` finishes any ReportPortal launches that were started.

## Responsibility Of Each ReportPortal-Related Class

### `ReportPortalConfig`

Location: `QaaS.Runner.Assertions/ConfigurationObjects/ReportPortalConfig.cs`

Responsibilities:

- Defines the YAML/config shape for ReportPortal reporting.
- Reads runtime-sensitive values from environment variables.
- Normalizes endpoint settings.
- Builds immutable `ReportPortalSettings`.
- Merges launch attributes from the run descriptor and static config.
- Keeps project override visible but ignored, because routing is by `MetaData.Team`.

### `ReportPortalSettings`

Location: nested in `ReportPortalConfig.cs`

Responsibilities:

- Holds the resolved runtime settings used by reporters and launch manager.
- Exposes `RequestedProjectName`, derived from team.
- Normalizes endpoint URI via `TryGetEndpointUri(...)`.
- Builds launch attributes for `StartLaunchRequest`.
- Builds the launch grouping key from endpoint, project, and system.

### `ReportPortalAccessValidator`

Location: `QaaS.Runner.Assertions/ReportPortalAccessValidator.cs`

Responsibilities:

- Checks whether publishing is possible.
- Verifies enabled state, team, endpoint, API key, and accessible project.
- Reads projects from the ReportPortal API using the provided API key.
- Matches the accessible project to `MetaData.Team`.
- Caches access checks by endpoint, API key, and team to avoid repeated calls.
- Logs warnings for access failures and returns a failure result instead of throwing.

### `ReportPortalAccessResult`

Location: nested in `ReportPortalAccessValidator.cs`

Responsibilities:

- Represents the outcome of access validation.
- Carries resolved endpoint URI, project name, API key, and failure reason.
- Gives the launch manager a simple `CanPublish` gate.

### `ReportPortalLaunchManager`

Location: `QaaS.Runner.Assertions/ReportPortalLaunchManager.cs`

Responsibilities:

- Owns ReportPortal launch lifecycle for one runner invocation.
- Starts a launch only after access validation passes.
- Reuses a live launch for the same endpoint/project/system group.
- Suppresses repeated launch-start failures for the same launch key.
- Finishes all started launches during runner teardown.
- Disposes ReportPortal client services.

### `ReportPortalLaunchContext`

Location: nested in `ReportPortalLaunchManager.cs`

Responsibilities:

- Carries the active ReportPortal client service, launch UUID, and launch start time to the reporter.
- Lets `ReportPortalReporter` publish items and logs without owning launch lifecycle.

### `ReportPortalRunDescriptor`

Location: `QaaS.Runner.Assertions/ReportPortalRunDescriptor.cs`

Responsibilities:

- Captures runner-scoped launch identity.
- Builds default launch name.
- Builds default launch description.
- Carries team, system, session names, execution mode, local start time, and launch attributes.
- Keeps launch naming stable enough for ReportPortal history and widgets to remain useful across reruns.

### `ReportPortalReporter`

Location: `QaaS.Runner.Assertions/ReportPortalReporter.cs`

Responsibilities:

- Publishes one QaaS assertion result into the shared ReportPortal launch.
- Starts a ReportPortal `TEST` item for the assertion.
- Builds stable identity and uses it as `UniqueId` and `TestCaseId`.
- Writes context, outcome, links, sessions, template, and assertion attachments as ReportPortal logs and log attachments.
- Finishes the ReportPortal item with a status mapped from QaaS assertion status.
- Catches publishing errors, logs a warning, and allows the QaaS run to continue.

Status mapping:

| QaaS assertion status | ReportPortal status |
|---|---|
| Passed | Passed |
| Failed | Failed |
| Broken | Interrupted |
| Unknown | Skipped |
| Skipped | Skipped |

## What Moved From Allure-Specific Logic To `BaseReporter`

The goal was to make the data preparation reporter-neutral. ReportPortal needs the same assertion/session/template/attachment data that Allure already saves, so shared helpers live in `BaseReporter`.

| Shared helper | What it does | Why it moved to base |
|---|---|---|
| `BuildAssertionArtifacts(...)` | Serializes custom assertion attachments into `ReportArtifact` objects. | Both Allure and ReportPortal need the same attachment content and content type. |
| `BuildTemplateArtifact()` | Produces the rendered config template as a YAML artifact. | Both reporters can attach or log the same template. |
| `BuildSessionArtifact(...)` | Serializes session data to JSON. | Session data should be consistent across reporters. |
| `BuildMetadataAttributes()` | Extracts metadata from `InternalContext`. | ReportPortal uses metadata for attributes and identity. |
| `ExtractMetadataAttributes(...)` | Flattens metadata and extra labels into key/value attributes. | Keeps metadata mapping deterministic and reusable. |
| `BuildAssertionContextArtifact(...)` | Creates a JSON context artifact for assertion, execution, sessions, links, and metadata. | ReportPortal attaches this as structured context, and it avoids rebuilding the payload in reporter-specific code. |
| `BuildStableReportPortalIdentity(...)` | Builds stable identity from team, system, case, assertion, sessions, and metadata. | Required for ReportPortal history via `testCaseId` and `UniqueId`. |
| `BuildAssertionTextDetails(...)` | Normalizes assertion message/trace and broken assertion details. | Both reporters need consistent message and trace behavior. |
| `BuildSessionSummaryText(...)` | Produces a readable session summary. | ReportPortal currently stores session info as logs. |
| `BuildActionFailureText(...)` | Produces readable failure detail text. | ReportPortal currently stores session failure details as error logs. |
| `BuildFlakinessText(...)` | Formats flaky reason details. | Both reports need human-readable flaky context. |
| `GetAttachmentTypeBySerializationType(...)` | Maps QaaS serialization types to MIME types. | Needed by both attachment writers. |

## Data Placement Table

This table shows where each known Allure-saved data item is saved in Allure and ReportPortal in the current implementation. A dash means it is not currently saved in that reporter.

| Data | Allure placement | ReportPortal placement |
|---|---|---|
| QaaS run grouping | Allure results folder and suite labels | Shared ReportPortal launch grouped by endpoint/project/system |
| Launch/run name | Derived from Allure result context | `StartLaunchRequest.Name` via `ReportPortalRunDescriptor` or config override |
| Launch/run description | - | `StartLaunchRequest.Description` via `ReportPortalRunDescriptor` or config override |
| Launch attributes | - | ReportPortal launch attributes from team, system, sessions, metadata, and static config |
| Assertion/test name | Allure test result `name` | ReportPortal `TEST` item `Name` |
| Assertion full/logical name | Allure `fullName` style field | ReportPortal item description and stable identity context |
| Assertion status | Allure test result `status` | ReportPortal item `Status` |
| Assertion start time | Allure test result `start` | ReportPortal item `StartTime` |
| Assertion end time | Allure test result `stop` | ReportPortal item `EndTime` |
| Stable history id | Allure `historyId` and rerun identity | `UniqueId` and `TestCaseId` |
| Code reference | - | ReportPortal `CodeReference` derived from stable identity |
| Assertion message | Allure `statusDetails.message` | ReportPortal description and outcome log |
| Assertion trace | Allure `statusDetails.trace` | ReportPortal outcome log |
| Broken assertion exception | Allure broken status details | ReportPortal outcome log, status mapped to `Interrupted` |
| Flaky flag | Allure `statusDetails.flaky` | ReportPortal description text, no dedicated first-class flag yet |
| Flakiness reasons | Allure test description | ReportPortal item description |
| Rendered assertion configuration YAML | Allure test description | ReportPortal item description |
| Session names | Allure test parameters | ReportPortal item parameters |
| Data source names | Allure test parameters | ReportPortal item parameters |
| Team metadata | Allure labels/context | ReportPortal parameters and attributes |
| System metadata | Allure labels/context | ReportPortal parameters and attributes |
| Execution id | Allure labels/tags | ReportPortal parameters and attributes |
| Case name | Allure labels/tags | ReportPortal parameters and attributes |
| Severity | Allure severity label | ReportPortal item attribute |
| Package label | Allure label | ReportPortal metadata attributes only if present in metadata |
| Test class label | Allure label | ReportPortal metadata attributes only if present in metadata |
| Test type label | Allure label | ReportPortal metadata attributes only if present in metadata |
| Epic label | Allure label | ReportPortal metadata attributes only if present in metadata |
| Feature label | Allure label | ReportPortal metadata attributes only if present in metadata |
| Host label | Allure label | ReportPortal metadata attributes only if present in metadata |
| QaaS tag | Allure tag | ReportPortal `tool=QaaS` attribute |
| Allure links | Allure first-class `links` | ReportPortal plain log text in `WriteLinksLog(...)` |
| Assertion context JSON | - | ReportPortal log attachment from `BuildAssertionContextArtifact(...)` |
| Session step | Allure step | ReportPortal session summary log on assertion item |
| Session status | Allure step status | ReportPortal log level inferred from session failures |
| Session start/end time | Allure step timing | Included in session summary text, not as item timing |
| Session inputs | Allure step parameters | Included in session summary text and session JSON attachment |
| Session outputs | Allure step parameters | Included in session summary text and session JSON attachment |
| Session failure group | Allure nested step group | ReportPortal error logs |
| Session failure action | Allure nested failure step | ReportPortal error log text |
| Session failure action type | Allure nested failure step description/parameter | ReportPortal error log text |
| Session failure reason | Allure nested failure parameter/text | ReportPortal error log text |
| Session data JSON | Allure attachment and raw `SessionsData/...` file | ReportPortal log attachment |
| Session log text | Allure attachment and raw `SessionLogs/...` file | - |
| Rendered template YAML attachment | Allure attachment and raw `Templates/.../template.yaml` file | ReportPortal log attachment |
| Custom assertion attachments | Allure attachments and raw `AssertionsAttachments/...` files | ReportPortal log attachments |
| Coverage XML attachments | Allure attachments from `Coverages/...` | - |
| Raw legacy `SessionsData/...` files | Files under `allure-results` | - |
| Raw legacy `SessionLogs/...` files | Files under `allure-results` | - |
| Raw legacy `Templates/...` files | Files under `allure-results` | - |
| Raw legacy `AssertionsAttachments/...` files | Files under `allure-results` | - |
| Raw legacy `Coverages/...` files | Files under `allure-results` | - |

## Known Gaps

The current ReportPortal implementation does not yet reach full parity with Allure.

Missing or partial pieces:

- Session log attachments are still Allure-only.
- Coverage XML attachments are still Allure-only.
- Allure's real step/substep hierarchy is currently flattened into ReportPortal logs.
- Session timing is not represented as real ReportPortal child item timing.
- Failure hierarchy is not represented as real ReportPortal child items.
- Allure links are first-class in Allure but plain text logs in ReportPortal.
- Flaky status is not represented as a dedicated ReportPortal attribute yet.
- Several Allure labels are only represented if they exist in metadata, not as one-to-one label translations.
- Legacy raw filesystem copies under `allure-results` have no ReportPortal equivalent.

## Suggested Next Steps

Recommended order for continuing the work:

1. Add session log attachments to `ReportPortalReporter`.
2. Add coverage XML attachment publishing to `ReportPortalReporter`.
3. Add explicit `flaky=true` ReportPortal attribute when applicable.
4. Improve link handling, either by richer log formatting or a first-class ReportPortal mapping if supported by the SDK.
5. Promote sessions from flat logs to nested ReportPortal child items.
6. Promote session failures from flat error logs to nested child items under sessions.
7. Add session child item start/end times from `SessionData.UtcStartTime` and `SessionData.UtcEndTime`.
8. Review label parity for package, test class, test type, epic, feature, and host.


## Developer Notes

- Keep ReportPortal passive. If publishing fails, warn and continue.
- Keep Allure behavior stable. The existing user experience should not change when ReportPortal is added.
- Do not let `ReportPortal.Project` override team-based routing unless the product decision changes.
- If adding nested ReportPortal items, be careful to preserve stable `TestCaseId` at the assertion level so history across reruns remains useful.
- Direct `ExecutionBuilder.Build()` paths may not have a `ReportPortalLaunchManager`. Those paths should not throw just because ReportPortal is enabled.
