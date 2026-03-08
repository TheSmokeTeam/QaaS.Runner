using Microsoft.Extensions.Logging;
using System.Text;
using ReportPortal.Client.Abstractions.Models;
using ReportPortal.Client.Abstractions.Requests;
using QaaS.Framework.Configurations;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.ConfigurationObjects;
using AssertionSeverity = QaaS.Runner.Assertions.AssertionObjects.AssertionSeverity;
using ReportPortalLogLevel = ReportPortal.Client.Abstractions.Models.LogLevel;

namespace QaaS.Runner.Assertions;

/// <summary>
/// Publishes QaaS runner assertion results into ReportPortal while preserving the existing Allure writer. Publishing is
/// best-effort: failures are logged as warnings and never change the runner exit code.
/// </summary>
public class ReportPortalReporter : BaseReporter
{
    private static readonly IDictionary<AssertionStatus, Status> AssertionStatusToReportPortalStatusMap =
        new Dictionary<AssertionStatus, Status>
        {
            { AssertionStatus.Passed, Status.Passed },
            { AssertionStatus.Failed, Status.Failed },
            { AssertionStatus.Broken, Status.Interrupted },
            { AssertionStatus.Unknown, Status.Skipped },
            { AssertionStatus.Skipped, Status.Skipped }
        };

    private static readonly IDictionary<AssertionSeverity, string> AssertionSeverityToAttributeValueMap =
        new Dictionary<AssertionSeverity, string>
        {
            { AssertionSeverity.Trivial, "trivial" },
            { AssertionSeverity.Minor, "minor" },
            { AssertionSeverity.Normal, "normal" },
            { AssertionSeverity.Critical, "critical" },
            { AssertionSeverity.Blocker, "blocker" }
        };

    public required ReportPortalSettings Settings { get; init; }
    public required ReportPortalLaunchManager LaunchManager { get; init; }

    /// <summary>
    /// Writes one runner-produced assertion result into the shared ReportPortal launch for the corresponding team project
    /// and system. Any publishing failure is downgraded to a warning so QaaS can continue running.
    /// </summary>
    public override void WriteTestResults(AssertionResult assertionResult)
    {
        ArgumentNullException.ThrowIfNull(assertionResult);

        try
        {
            WriteTestResultsCore(assertionResult);
        }
        catch (Exception exception)
        {
            Context.Logger.LogWarning(exception,
                "Could not publish assertion {AssertionName} to ReportPortal for team {TeamName} and system {SystemName}. The run will continue.",
                assertionResult.Assertion.Name,
                Settings.Team ?? "<missing-team>",
                Settings.System);
        }
    }

    private void WriteTestResultsCore(AssertionResult assertionResult)
    {
        var launch = LaunchManager.EnsureLaunchStartedAsync(Settings, Context.Logger).GetAwaiter().GetResult();
        if (launch is null)
            return;

        var requestedStartTimeUtc = GetAssertionStartTime(assertionResult);
        var startTimeUtc = requestedStartTimeUtc < launch.LaunchStartTimeUtc
            ? launch.LaunchStartTimeUtc
            : requestedStartTimeUtc;
        var finishTimeUtc = startTimeUtc.AddMilliseconds(Math.Max(assertionResult.TestDurationMs, 1));
        var itemAttributes = BuildItemAttributes(assertionResult);
        var stableIdentity = BuildStableReportPortalIdentity(assertionResult,
            Settings.Team ?? "Unknown Team",
            Settings.System);
        var itemUuid = launch.Service.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launch.LaunchUuid,
            Name = assertionResult.Assertion.Name,
            Description = BuildDescription(assertionResult),
            StartTime = startTimeUtc,
            Type = TestItemType.Test,
            UniqueId = stableIdentity,
            TestCaseId = stableIdentity,
            CodeReference = BuildCodeReference(stableIdentity),
            Parameters = BuildParameters(assertionResult),
            Attributes = itemAttributes
        }).GetAwaiter().GetResult().Uuid;

        try
        {
            WriteAssertionContextLog(launch, itemUuid, assertionResult, stableIdentity);
            WriteAssertionOutcomeLog(launch, itemUuid, assertionResult);
            WriteLinksLog(launch, itemUuid, assertionResult);
            WriteSessionLogs(launch, itemUuid, assertionResult);
            WriteTemplateAttachment(launch, itemUuid);
            WriteAssertionAttachments(launch, itemUuid, assertionResult);

            launch.Service.TestItem.FinishAsync(itemUuid, new FinishTestItemRequest
            {
                LaunchUuid = launch.LaunchUuid,
                EndTime = finishTimeUtc,
                Status = AssertionStatusToReportPortalStatusMap[assertionResult.AssertionStatus],
                Description = BuildDescription(assertionResult),
                Attributes = itemAttributes
            }).GetAwaiter().GetResult();
        }
        catch
        {
            TryFinishAsFailed(launch, itemUuid);
            throw;
        }
    }

    private void WriteAssertionContextLog(ReportPortalLaunchContext launch, string itemUuid, AssertionResult assertionResult,
        string stableIdentity)
    {
        var metadataAttributes = BuildMetadataAttributes();
        var contextText = new StringBuilder()
            .AppendLine("Assertion context:")
            .AppendLine($"- Stable identity: {stableIdentity}")
            .AppendLine($"- Team: {Settings.Team ?? "<missing-team>"}")
            .AppendLine($"- System: {Settings.System}")
            .AppendLine($"- ExecutionId: {Context.ExecutionId ?? "<none>"}")
            .AppendLine($"- CaseName: {Context.CaseName ?? "<none>"}")
            .AppendLine($"- Sessions: {assertionResult.Assertion.SessionDataList.Count}")
            .AppendLine($"- Data sources: {assertionResult.Assertion.DataSourceList?.Count ?? 0}")
            .AppendLine()
            .AppendLine("Metadata:")
            .AppendLine(BuildMetadataSummaryText(metadataAttributes))
            .ToString()
            .Trim();

        CreateLogItem(launch, itemUuid, ReportPortalLogLevel.Info, contextText,
            BuildAssertionContextArtifact(assertionResult, Settings.Team, Settings.System));
    }

    private void WriteAssertionOutcomeLog(ReportPortalLaunchContext launch, string itemUuid, AssertionResult assertionResult)
    {
        var logLevel = assertionResult.AssertionStatus switch
        {
            AssertionStatus.Passed => ReportPortalLogLevel.Info,
            AssertionStatus.Skipped => ReportPortalLogLevel.Warning,
            AssertionStatus.Unknown => ReportPortalLogLevel.Warning,
            AssertionStatus.Failed => ReportPortalLogLevel.Error,
            AssertionStatus.Broken => ReportPortalLogLevel.Error,
            _ => ReportPortalLogLevel.Info
        };

        var assertionTextDetails = BuildAssertionTextDetails(assertionResult);
        var text = new StringBuilder()
            .AppendLine($"Assertion status: {assertionResult.AssertionStatus}")
            .AppendLine()
            .AppendLine(string.IsNullOrWhiteSpace(assertionTextDetails.Message)
                ? "No assertion message was provided."
                : assertionTextDetails.Message);

        if (!string.IsNullOrWhiteSpace(assertionTextDetails.Trace))
        {
            text.AppendLine()
                .AppendLine(assertionTextDetails.Trace);
        }

        CreateLogItem(launch, itemUuid, logLevel, text.ToString().Trim(), null);
    }

    private void WriteLinksLog(ReportPortalLaunchContext launch, string itemUuid, AssertionResult assertionResult)
    {
        if (assertionResult.Links is null)
            return;

        var links = assertionResult.Links.ToList();
        if (links.Count == 0)
            return;

        var text = new StringBuilder("Attached links:")
            .AppendLine()
            .AppendJoin(Environment.NewLine, links.Select(link => $"- {link.Key}: {link.Value}"))
            .ToString();

        CreateLogItem(launch, itemUuid, ReportPortalLogLevel.Info, text, null);
    }

    private void WriteSessionLogs(ReportPortalLaunchContext launch, string itemUuid, AssertionResult assertionResult)
    {
        foreach (var sessionData in assertionResult.Assertion.SessionDataList)
        {
            var summary = BuildSessionSummaryText(sessionData);
            var sessionArtifact = BuildSessionArtifact(sessionData);
            CreateLogItem(launch, itemUuid,
                sessionData.SessionFailures.Any() ? ReportPortalLogLevel.Error : ReportPortalLogLevel.Info,
                summary,
                sessionArtifact);

            foreach (var actionFailure in sessionData.SessionFailures)
            {
                CreateLogItem(launch, itemUuid, ReportPortalLogLevel.Error,
                    BuildActionFailureText(sessionData, actionFailure), null);
            }
        }
    }

    private void WriteTemplateAttachment(ReportPortalLaunchContext launch, string itemUuid)
    {
        var templateArtifact = BuildTemplateArtifact();
        if (templateArtifact is null)
            return;

        CreateLogItem(launch, itemUuid, ReportPortalLogLevel.Info, "Execution configuration template.",
            templateArtifact);
    }

    private void WriteAssertionAttachments(ReportPortalLaunchContext launch, string itemUuid, AssertionResult assertionResult)
    {
        foreach (var artifact in BuildAssertionArtifacts(assertionResult))
        {
            CreateLogItem(launch, itemUuid, ReportPortalLogLevel.Info,
                $"Assertion attachment: {artifact.RelativePath}",
                artifact);
        }
    }

    private void CreateLogItem(ReportPortalLaunchContext launch, string itemUuid, ReportPortalLogLevel level,
        string text,
        ReportArtifact? artifact)
    {
        var request = new CreateLogItemRequest
        {
            LaunchUuid = launch.LaunchUuid,
            TestItemUuid = itemUuid,
            Level = level,
            Text = text,
            Time = DateTime.UtcNow
        };

        if (artifact is not null)
        {
            request.Attach = new LogItemAttach(artifact.ContentType ?? RawDataAttachmentType, artifact.Content)
            {
                Name = Path.GetFileName(artifact.Name)
            };
        }

        launch.Service.LogItem.CreateAsync(request).GetAwaiter().GetResult();
    }

    private static DateTime GetAssertionStartTime(AssertionResult assertionResult)
    {
        if (assertionResult.Assertion.SessionDataList.Any())
            return assertionResult.Assertion.SessionDataList.Min(sessionData => sessionData.UtcStartTime);

        return DateTime.UtcNow;
    }

    private IList<KeyValuePair<string, string>> BuildParameters(AssertionResult assertionResult)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("Session Names",
                $"[{string.Join(", ", assertionResult.Assertion.SessionDataList.Select(session => session.Name))}]"),
            new("Data Sources",
                $"[{string.Join(", ", assertionResult.Assertion.DataSourceList?.Select(dataSource => dataSource.Name) ?? [])}]")
        };

        if (!string.IsNullOrWhiteSpace(Settings.Team))
            parameters.Add(new KeyValuePair<string, string>("Team", Settings.Team));
        if (!string.IsNullOrWhiteSpace(Settings.System))
            parameters.Add(new KeyValuePair<string, string>("System", Settings.System));
        if (!string.IsNullOrWhiteSpace(Context.ExecutionId))
            parameters.Add(new KeyValuePair<string, string>("Execution Id", Context.ExecutionId));
        if (!string.IsNullOrWhiteSpace(Context.CaseName))
            parameters.Add(new KeyValuePair<string, string>("Case Name", Context.CaseName));

        foreach (var metadataAttribute in BuildMetadataAttributes()
                     .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key)))
        {
            parameters.Add(new KeyValuePair<string, string>(metadataAttribute.Key.Trim(),
                metadataAttribute.Value?.Trim() ?? string.Empty));
        }

        return parameters;
    }

    private IList<ItemAttribute> BuildItemAttributes(AssertionResult assertionResult)
    {
        var attributes = new List<ItemAttribute>
        {
            new()
            {
                Key = "tool",
                Value = QaaSTag
            },
            new()
            {
                Key = "assertion",
                Value = assertionResult.Assertion.AssertionName
            },
            new()
            {
                Key = "severity",
                Value = AssertionSeverityToAttributeValueMap[Severity]
            }
        };

        if (!string.IsNullOrWhiteSpace(Settings.Team))
        {
            attributes.Add(new ItemAttribute
            {
                Key = "team",
                Value = Settings.Team
            });
        }

        if (!string.IsNullOrWhiteSpace(Settings.System))
        {
            attributes.Add(new ItemAttribute
            {
                Key = "system",
                Value = Settings.System
            });
        }

        foreach (var sessionName in assertionResult.Assertion.SessionDataList
                     .Select(session => session.Name)
                     .Where(sessionName => !string.IsNullOrWhiteSpace(sessionName))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            attributes.Add(new ItemAttribute
            {
                Key = "session",
                Value = sessionName
            });
        }

        if (!string.IsNullOrWhiteSpace(Context.ExecutionId))
        {
            attributes.Add(new ItemAttribute
            {
                Key = "executionId",
                Value = Context.ExecutionId
            });
        }

        if (!string.IsNullOrWhiteSpace(Context.CaseName))
        {
            attributes.Add(new ItemAttribute
            {
                Key = "caseName",
                Value = Context.CaseName
            });
        }

        foreach (var attribute in Settings.Attributes.Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key)))
        {
            attributes.Add(new ItemAttribute
            {
                Key = attribute.Key.Trim(),
                Value = attribute.Value?.Trim() ?? string.Empty
            });
        }

        foreach (var metadataAttribute in BuildMetadataAttributes()
                     .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key)))
        {
            attributes.Add(new ItemAttribute
            {
                Key = metadataAttribute.Key.Trim(),
                Value = metadataAttribute.Value?.Trim() ?? string.Empty
            });
        }

        return attributes;
    }

    private string BuildDescription(AssertionResult assertionResult)
    {
        var assertionTextDetails = BuildAssertionTextDetails(assertionResult);
        var metadataAttributes = BuildMetadataAttributes();
        var description = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(assertionTextDetails.Message))
        {
            description.AppendLine("Assertion message:")
                .AppendLine(assertionTextDetails.Message.Trim());
        }

        description.AppendLine()
            .AppendLine("Execution context:")
            .AppendLine($"- Team: {Settings.Team ?? "<missing-team>"}")
            .AppendLine($"- System: {Settings.System}")
            .AppendLine($"- Execution Id: {Context.ExecutionId ?? "<none>"}")
            .AppendLine($"- Case Name: {Context.CaseName ?? "<none>"}")
            .AppendLine($"- Sessions: {string.Join(", ", assertionResult.Assertion.SessionDataList.Select(session => session.Name))}")
            .AppendLine($"- Data Sources: {string.Join(", ", assertionResult.Assertion.DataSourceList?.Select(dataSource => dataSource.Name) ?? [])}");

        description.AppendLine()
            .AppendLine("Metadata attributes:")
            .AppendLine("```text")
            .AppendLine(BuildMetadataSummaryText(metadataAttributes))
            .AppendLine("```");

        description.AppendLine()
            .AppendLine("Assertion configuration:")
            .AppendLine("```yaml")
            .AppendLine(assertionResult.Assertion.AssertionConfiguration.BuildConfigurationAsYaml())
            .AppendLine("```");

        if (assertionResult.Flaky.IsFlaky)
        {
            description.AppendLine()
                .AppendLine(BuildFlakinessText(assertionResult.Flaky.FlakinessReasons));
        }

        return description.ToString().Trim();
    }

    private static string BuildCodeReference(string stableIdentity)
    {
        return $"qaas/{stableIdentity.Replace("::", "/", StringComparison.Ordinal)}";
    }

    private void TryFinishAsFailed(ReportPortalLaunchContext launch, string itemUuid)
    {
        try
        {
            launch.Service.TestItem.FinishAsync(itemUuid, new FinishTestItemRequest
            {
                LaunchUuid = launch.LaunchUuid,
                EndTime = DateTime.UtcNow,
                Status = Status.Failed
            }).GetAwaiter().GetResult();
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}
