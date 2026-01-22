# QaaS.Runner.Assertions

Responsible for handling all configured assertions, running them and saving their results in a displayable manner.

Currently supports saving the results as allure files that can be displayed with the allure CLI.

Contains the `Assertions` configuration section configuration objects.

[TOC]

## Relevant hooks

Uses the `IAssertion` hook from the `QaaS.SDK` for running the assertions.

**Providing assertions**
If a user provides 2 assertions with the same name only one of them is used.
