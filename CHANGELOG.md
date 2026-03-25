# Changelog

This file is generated automatically from CI from git history.

## Unreleased

- fix(bootstrap): resolve implicit config from app base dir
- fix(runner): restore yaml diagnostics for malformed configs
- fix(bootstrap): infer run mode for code-first hosts
- test(loaders): cover malformed yaml diagnostics
- fix(runner): clarify invalid configuration diagnostics
- docs(changelog): add automated changelog
- refactor(config): expose runner configuration getters
- docs(cli): fix runner push-reference wording
- chore(deps): update framework executions to 1.2.7
- chore(deps): update framework executions to 1.2.6
- docs(runner): annotate public configuration functions
- fix(configuration): internalize runner builder configs
- fix(runner): support internal configurator discovery
- chore: consume framework 1.2.5-alpha.4 and comm objects 2.0.2-alpha.4
- feat(runner): support discovered code configurators
- docs(schema): document runner session stage fields

## 4.1.1-alpha.1 - 2026-03-22

- No changes recorded.

## 4.1.1 - 2026-03-23

- docs(changelog): add automated changelog
- refactor(config): expose runner configuration getters
- docs(cli): fix runner push-reference wording
- chore(deps): update framework executions to 1.2.7
- chore(deps): update framework executions to 1.2.6
- docs(runner): annotate public configuration functions
- fix(configuration): internalize runner builder configs
- fix(runner): support internal configurator discovery
- chore: consume framework 1.2.5-alpha.4 and comm objects 2.0.2-alpha.4
- feat(runner): support discovered code configurators
- docs(schema): document runner session stage fields

## 4.1.0-alpha.27 - 2026-03-22

- fix(runner): support internal configurator discovery

## 4.1.0-alpha.26 - 2026-03-22

- chore: consume framework 1.2.5-alpha.4 and comm objects 2.0.2-alpha.4
- feat(runner): support discovered code configurators
- chore(deps): bump framework and mocker packages

## 4.1.0-alpha.25 - 2026-03-21

- fix(runner): preserve binary payloads without target type

## 4.1.0-alpha.24 - 2026-03-21

- chore(deps): bump framework and mocker alphas
- feat(runner): add log report toggle and overwrite folders

## 4.1.0-alpha.23 - 2026-03-21

- chore(deps): bump mocker communicationobjects alpha
- feat(config): align builder updates with framework helpers
- removed useless file
- fix(validation): require metadata for executions
- test(metadata): guard no-metadata fallback logging
- refactor(builders): align fluent crud conventions
- fix(validation): enforce chunk protocol compatibility
- fix(template): preserve sparse configuration indexes
- fix(infrastructure): serialize scoped session log store creation
- fix(runner): harden lifecycle, sessions, and allure reporting

## 4.1.0-alpha.22 - 2026-03-17

- Fix consume results for mocker commands
- fix: allure reporting with history and formatting and dep updating

## 4.1.0-alpha.21 - 2026-03-17

- Fix consume results for mocker commands

## 4.1.0-alpha.20 - 2026-03-17

- fix: allure reporting with history and formatting and dep updating
- test: reduce runner test logging overhead
- chore(deps): update qaas.framework.executions to 1.2.4

## 4.1.0-alpha.19 - 2026-03-17

- No changes recorded.

## 4.1.0-alpha.18 - 2026-03-17

- chore(deps): use qaas framework 1.2.4-alpha.1
- fix(allure): remove duplicate attachment copies
- fix(allure): restore legacy attachment folders
- chore: framework version
- fix(runtime): clarify probe loading and session materialization
- fix: structure session lifecycle logs

## 4.1.0-alpha.17 - 2026-03-17

- fix: internal communication data and session tests
- fix: patch session and mocker runtime regressions
- fix(runner): align bootstrap with framework alpha validation
- refactor(runner): simplify bootstrap and builder validation
- test(coverage): deepen runtime guard coverage
- fix(runner): harden bootstrap and test runtime paths
- refactor(sessions): align runtime configuration surface
- refactor(runner): clean bootstrap and session overrides
- fix(configuration): preserve sparse update values
- refactor(builders): unify runtime configuration updates
- docs(infrastructure): improve shared utility documentation
- chore: remvoed useless files from e2e

## 4.1.0-alpha.16 - 2026-03-16

- chore: remove runner pr docs
- chore: update package dependencies
- fix: adopt qaas framework 1.2.0
- fix: scope execution artifacts per run
- Speed up session test suites
- test: improve session coverage tests
- fix(runner): harden sessions and mocker command flow
- fix: probes bugs for big z
- feat: added session logs to allure and fixed template bug
- fix(rabbitmq): restore overlapping session stages
- update: e2e configuration
- fix(ci): restore from valid package sources
- Add configurable runner process-exit behavior
- fix: align mocker IO state with qaas framework
- test: add metadata and rabbit e2e fixtures
- fix: prevent duplicate metadata validation errors
- docs: document architecture sweep changes
- fix: improve runtime logging readability
- test: add metadata and path safety regressions
- fix: harden metadata reporting and storage flows
- test: add regression coverage for lifecycle and concurrency
- fix: harden execution and session runtime
- chore: remove shit
- feat(config): merge runner hook configuration upserts
- feat(builders): add runtime configuration upsert support
- fix(ci): vendor unpublished qaas packages
- chore(deps): align internal package references
- refactor(execution): remove unused logic shouldrun checks
- feat(config): merge runner hook configuration upserts
- chore(deps): update runner stable dependencies
- chore(deps): align runner internal package references
- test(e2e): simplify runner rabbit smoke coverage
- feat(builders): add runtime configuration upsert support
- feat(cases): add exact and regex ignore filters
- fix(probes): scope runner probe hook resolution by session
- fix(builder): avoid duplicate metadata validation warnings
- ci: publish badges from master workflows
- ci: publish coverage badges from master only
- ci: scope solution coverage to product assemblies

## 4.1.0-alpha.15 - 2026-03-15

- ci: split filtered coverage badges
- ci: publish portable symbols
- ci: sanitize gist badge credentials
- ci: trim coverage badge token
- docs: add workflow and coverage badges

## 4.1.0-alpha.14 - 2026-03-15

- ci: use supported dotnet-coverage flags
- ci: order dotnet-coverage arguments correctly
- ci: fix dotnet-coverage command invocation
- ci: add coverage reporting and robust package parsing
- ci: stage packed packages in restore artifact
- Allow CI on any tag

## 4.1.0-alpha.13 - 2026-03-11

- chore(deps): align internal package references

## 4.1.0-alpha.12 - 2026-03-11

- refactor(execution): remove unused logic shouldrun checks

## 4.1.0-alpha.11 - 2026-03-11

- feat(config): merge runner hook configuration upserts

## 4.1.0-alpha.10 - 2026-03-11

- chore(deps): update runner stable dependencies

## 4.1.0-alpha.9 - 2026-03-11

- chore(deps): align runner internal package references

## 4.1.0-alpha.8 - 2026-03-11

- test(e2e): simplify runner rabbit smoke coverage

## 4.1.0-alpha.7 - 2026-03-11

- feat(builders): add runtime configuration upsert support

## 4.1.0-alpha.6 - 2026-03-11

- feat(cases): add exact and regex ignore filters

## 4.1.0-alpha.5 - 2026-03-11

- fix(probes): scope runner probe hook resolution by session

## 4.1.0-alpha.4 - 2026-03-11

- fix(builder): avoid duplicate metadata validation warnings
- Restrict tag pipeline to stable releases

## 4.1.0-alpha.3 - 2026-03-11

- ci: publish restored package artifact for mirror polling
- feat: publish runner component packages
- fix: include runner runtime package dependencies
- Update framework dependency
- build: use stable framework and simplify nuget push
- docs: remove architecture section from readme
- Configure Dependabot for NuGet with monthly updates
- docs: align README format and nuget publish metadata

## 4.1.0-alpha.2 - 2026-03-06

- build(deps): align framework packages with validated tag
- docs: create unified solution README with badges and coverage
- Fix probe hook collisions across sessions and add regressions

## 4.1.0-alpha.1 - 2026-03-05

- No changes recorded.

## 4.1.0 - 2026-03-21

- chore(deps): bump framework and mocker packages
- fix(runner): preserve binary payloads without target type
- chore(deps): bump framework and mocker alphas
- feat(runner): add log report toggle and overwrite folders
- chore(deps): bump mocker communicationobjects alpha
- feat(config): align builder updates with framework helpers
- removed useless file
- fix(validation): require metadata for executions
- test(metadata): guard no-metadata fallback logging
- refactor(builders): align fluent crud conventions
- fix(validation): enforce chunk protocol compatibility
- fix(template): preserve sparse configuration indexes
- fix(infrastructure): serialize scoped session log store creation
- fix(runner): harden lifecycle, sessions, and allure reporting
- Fix consume results for mocker commands
- Fix consume results for mocker commands
- fix: allure reporting with history and formatting and dep updating
- test: reduce runner test logging overhead
- chore(deps): update qaas.framework.executions to 1.2.4
- chore(deps): use qaas framework 1.2.4-alpha.1
- fix(allure): remove duplicate attachment copies
- fix(allure): restore legacy attachment folders
- chore: framework version
- fix(runtime): clarify probe loading and session materialization
- fix: structure session lifecycle logs
- fix: internal communication data and session tests
- fix: patch session and mocker runtime regressions
- fix(runner): align bootstrap with framework alpha validation
- refactor(runner): simplify bootstrap and builder validation
- test(coverage): deepen runtime guard coverage
- fix(runner): harden bootstrap and test runtime paths
- refactor(sessions): align runtime configuration surface
- refactor(runner): clean bootstrap and session overrides
- fix(configuration): preserve sparse update values
- refactor(builders): unify runtime configuration updates
- docs(infrastructure): improve shared utility documentation
- chore: remvoed useless files from e2e
- chore: remove runner pr docs
- chore: update package dependencies
- fix: adopt qaas framework 1.2.0
- fix: scope execution artifacts per run
- Speed up session test suites
- test: improve session coverage tests
- fix(runner): harden sessions and mocker command flow
- fix: probes bugs for big z
- feat: added session logs to allure and fixed template bug
- fix(rabbitmq): restore overlapping session stages
- update: e2e configuration
- fix(ci): restore from valid package sources
- Add configurable runner process-exit behavior
- fix: align mocker IO state with qaas framework
- test: add metadata and rabbit e2e fixtures
- fix: prevent duplicate metadata validation errors
- docs: document architecture sweep changes
- fix: improve runtime logging readability
- test: add metadata and path safety regressions
- fix: harden metadata reporting and storage flows
- test: add regression coverage for lifecycle and concurrency
- fix: harden execution and session runtime
- chore: remove shit
- feat(config): merge runner hook configuration upserts
- feat(builders): add runtime configuration upsert support
- fix(ci): vendor unpublished qaas packages
- chore(deps): align internal package references
- refactor(execution): remove unused logic shouldrun checks
- feat(config): merge runner hook configuration upserts
- chore(deps): update runner stable dependencies
- chore(deps): align runner internal package references
- test(e2e): simplify runner rabbit smoke coverage
- feat(builders): add runtime configuration upsert support
- feat(cases): add exact and regex ignore filters
- fix(probes): scope runner probe hook resolution by session
- fix(builder): avoid duplicate metadata validation warnings
- ci: publish badges from master workflows
- ci: publish coverage badges from master only
- ci: scope solution coverage to product assemblies
- ci: split filtered coverage badges
- ci: publish portable symbols
- ci: sanitize gist badge credentials
- ci: trim coverage badge token
- docs: add workflow and coverage badges
- ci: use supported dotnet-coverage flags
- ci: order dotnet-coverage arguments correctly
- ci: fix dotnet-coverage command invocation
- ci: add coverage reporting and robust package parsing
- ci: stage packed packages in restore artifact
- Allow CI on any tag
- Restrict tag pipeline to stable releases
- ci: publish restored package artifact for mirror polling
- feat: publish runner component packages
- fix: include runner runtime package dependencies
- Update framework dependency
- build: use stable framework and simplify nuget push
- docs: remove architecture section from readme
- Configure Dependabot for NuGet with monthly updates
- docs: align README format and nuget publish metadata
- build(deps): align framework packages with validated tag
- docs: create unified solution README with badges and coverage
- Fix probe hook collisions across sessions and add regressions
- refactor: mocker usage from framework and some more logic tests

## 4.0.0-alpha.8 - 2026-03-05

- chore(ci,test,docs): skip duplicate CI runs and improve mocker/session coverage

## 4.0.0-alpha.7 - 2026-03-05

- Vendor Qaas.Mocker.CommunicationObjects into repo
- Bump QaaS.Framework.Executions to 1.1.0-alpha.3

## 4.0.0-alpha.6 - 2026-03-05

- fix(sessions): harden mocker command flow and speed redis retries
- test: add branch-focused coverage for sessions builders and mocker commands
- refactor(runner): make lifecycle exit and allure serving overridable
- test(runner): expand coverage for loaders, runner, and builder branches
- test(sessions): add branch and internal coverage for session actions
- chore(coverage): exclude e2e assembly from test coverage
- test(session-logic): add branch coverage for staging and blockers
- docs(logics): improve XML docs for runtime logic APIs
- Fix: Removed build warnings
- test(builders): add CRUD and regression coverage for builder logic
- feat(builders): add CRUD APIs and strengthen builder validation

## 4.0.0-alpha.5 - 2026-03-04

- Added more tests and fixed allure not working

## 4.0.0-alpha.4 - 2026-03-04

- Fix: no building in the ci

## 4.0.0-alpha.3 - 2026-03-04

- Made only QaaS.Runner pushed in ci

## 4.0.0-alpha.2 - 2026-03-03

- No changes recorded.

## 4.0.0-alpha.1 - 2026-03-03

- Fixed tests
- Got rid of build warnings
- Fixed CI and added functionality tests
- Removed closed lib wrapped and redacted files
- Align CI pipeline and add Artifactory NuGet publishing
- first commit

