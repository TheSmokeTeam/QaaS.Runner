using System.Text;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Configurations;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;
using ReportPortal.Client.Abstractions.Models;
using ReportPortal.Client.Abstractions.Requests;
using AssertionSeverity = QaaS.Runner.Assertions.AssertionObjects.AssertionSeverity;
using ReportPortalLogLevel = ReportPortal.Client.Abstractions.Models.LogLevel;

namespace QaaS.Runner.Assertions.Reporters.ReportPortal;

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
            { AssertionStatus.Unknown, Status.Info },
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
    
    private readonly Lock _queuedResultsLock = new();
    private readonly List<AssertionResult> _queuedResults = [];
    public required ReportPortalConfig Config { get; init; }
    public string ExecutionMode { get; init; } = "run";

    /// <summary>
    /// Queues one runner-produced assertion result for final ReportPortal publishing.
    /// </summary>
    public override void WriteTestResults(AssertionResult assertionResult)
    {
        ArgumentNullException.ThrowIfNull(assertionResult);

        lock (_queuedResultsLock)
        {
            _queuedResults.Add(assertionResult);
        }
    }

    internal IReadOnlyList<AssertionResult> GetQueuedResultsSnapshot()
    {
        lock (_queuedResultsLock)
        {
            return _queuedResults.ToArray();
        }
    }

    internal void PublishQueuedResults(ReportPortalPublishContext publishContext,
        IReadOnlyList<AssertionResult> assertionResults,
        ILogger logger)
    {
        foreach (var assertionResult in assertionResults)
        {
            try
            {
                PublishTestResultCore(publishContext, assertionResult);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Could not publish assertion {AssertionName} to ReportPortal for team {TeamName} and system {SystemName}. The run will continue.",
                    assertionResult.Assertion.Name,
                    publishContext.LaunchPlan.Team ?? "<missing-team>",
                    publishContext.LaunchPlan.System);
            }
        }
    }

    private void PublishTestResultCore(ReportPortalPublishContext launch, AssertionResult assertionResult)
    {
        var requestedStartTimeUtc = GetAssertionStartTime(assertionResult);
        var startTimeUtc = requestedStartTimeUtc < launch.LaunchStartTimeUtc
            ? launch.LaunchStartTimeUtc
            : requestedStartTimeUtc;
        var finishTimeUtc = startTimeUtc.AddMilliseconds(Math.Max(assertionResult.TestDurationMs, 1));
        var launchPlan = launch.LaunchPlan;
        var itemAttributes = BuildItemAttributes(assertionResult, launchPlan);
        var stableIdentity = BuildStableReportPortalIdentity(assertionResult,
            launchPlan.Team ?? "Unknown Team",
            launchPlan.System);
        var itemUuid = launch.Service.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launch.LaunchUuid,
            Name = assertionResult.Assertion.Name,
            Description = BuildDescription(assertionResult, launchPlan),
            StartTime = startTimeUtc,
            Type = TestItemType.Test,
            UniqueId = stableIdentity,
            TestCaseId = stableIdentity,
            CodeReference = BuildCodeReference(stableIdentity),
            Parameters = BuildParameters(assertionResult, launchPlan),
            Attributes = itemAttributes
        }).GetAwaiter().GetResult().Uuid;

        try
        {
            WriteAssertionContextLog(launch, itemUuid, assertionResult, stableIdentity);
            WriteAssertionOutcomeLog(launch, itemUuid, assertionResult);
            WriteLinksLog(launch, itemUuid, assertionResult);
            WriteSessionDetails(launch, itemUuid, assertionResult);
            WriteSessionLogAttachments(launch, itemUuid, assertionResult);
            WriteTemplateAttachment(launch, itemUuid, assertionResult);
            WriteAssertionAttachments(launch, itemUuid, assertionResult);

            launch.Service.TestItem.FinishAsync(itemUuid, new FinishTestItemRequest
            {
                LaunchUuid = launch.LaunchUuid,
                EndTime = finishTimeUtc,
                Status = AssertionStatusToReportPortalStatusMap[assertionResult.AssertionStatus],
                Description = BuildDescription(assertionResult, launchPlan),
                Attributes = itemAttributes
            }).GetAwaiter().GetResult();
        }
        catch
        {
            TryFinishAsFailed(launch, itemUuid);
            throw;
        }
    }

    private void WriteAssertionContextLog(ReportPortalPublishContext launch, string itemUuid, AssertionResult assertionResult,
        string stableIdentity)
    {
        var launchPlan = launch.LaunchPlan;
        var metadataAttributes = BuildMetadataAttributes();
        var contextText = new StringBuilder()
            .AppendLine("Assertion context:")
            .AppendLine($"- Stable identity: {stableIdentity}")
            .AppendLine($"- Team: {launchPlan.Team ?? "<missing-team>"}")
            .AppendLine($"- Project: {launchPlan.Project ?? "<missing-project>"}")
            .AppendLine($"- System: {launchPlan.System}")
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
            BuildAssertionContextArtifact(assertionResult, launchPlan.Team, launchPlan.System));
    }

    private void WriteAssertionOutcomeLog(ReportPortalPublishContext launch, string itemUuid, AssertionResult assertionResult)
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

    private void WriteLinksLog(ReportPortalPublishContext launch, string itemUuid, AssertionResult assertionResult)
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

    private void WriteSessionDetails(ReportPortalPublishContext launch, string itemUuid, AssertionResult assertionResult)
    {
        foreach (var sessionData in assertionResult.Assertion.SessionDataList)
        {
            var summary = BuildSessionSummaryText(sessionData);
            var sessionArtifact = BuildSessionArtifact(sessionData, assertionResult.Assertion);
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

    private void WriteSessionLogAttachments(ReportPortalPublishContext launch, string itemUuid,
        AssertionResult assertionResult)
    {
        foreach (var sessionData in assertionResult.Assertion.SessionDataList)
        {
            var sessionLogArtifact = BuildSessionLogArtifact(sessionData, assertionResult.Assertion);
            if (sessionLogArtifact is null)
                continue;

            CreateLogItem(launch, itemUuid, ReportPortalLogLevel.Info,
                $"Session log: {sessionData.Name}",
                sessionLogArtifact);
        }
    }

    private void WriteTemplateAttachment(ReportPortalPublishContext launch, string itemUuid,
        AssertionResult assertionResult)
    {
        var templateArtifact = BuildTemplateArtifact(assertionResult.Assertion);
        if (templateArtifact is null)
            return;

        CreateLogItem(launch, itemUuid, ReportPortalLogLevel.Info, "Execution configuration template.",
            templateArtifact);
    }

    private void WriteAssertionAttachments(ReportPortalPublishContext launch, string itemUuid, AssertionResult assertionResult)
    {
        foreach (var artifact in BuildAssertionArtifacts(assertionResult))
        {
            CreateLogItem(launch, itemUuid, ReportPortalLogLevel.Info,
                $"Assertion attachment: {artifact.RelativePath}",
                artifact);
        }
    }

    private void CreateLogItem(ReportPortalPublishContext launch, string itemUuid, ReportPortalLogLevel level,
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

    private IList<KeyValuePair<string, string>> BuildParameters(AssertionResult assertionResult,
        ReportPortalLaunchPlan launchPlan)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("Session Names",
                $"[{string.Join(", ", assertionResult.Assertion.SessionDataList.Select(session => session.Name))}]"),
            new("Data Sources",
                $"[{string.Join(", ", assertionResult.Assertion.DataSourceList?.Select(dataSource => dataSource.Name) ?? [])}]")
        };

        if (!string.IsNullOrWhiteSpace(launchPlan.Team))
            parameters.Add(new KeyValuePair<string, string>("Team", launchPlan.Team));
        if (!string.IsNullOrWhiteSpace(launchPlan.Project))
            parameters.Add(new KeyValuePair<string, string>("Project", launchPlan.Project));
        if (!string.IsNullOrWhiteSpace(launchPlan.System))
            parameters.Add(new KeyValuePair<string, string>("System", launchPlan.System));
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

    private IList<ItemAttribute> BuildItemAttributes(AssertionResult assertionResult, ReportPortalLaunchPlan launchPlan)
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
                Value = AssertionSeverityToAttributeValueMap[ResolveSeverity(assertionResult.Assertion)]
            }
        };

        if (!string.IsNullOrWhiteSpace(launchPlan.Team))
        {
            attributes.Add(new ItemAttribute
            {
                Key = "team",
                Value = launchPlan.Team
            });
        }

        if (!string.IsNullOrWhiteSpace(launchPlan.Project))
        {
            attributes.Add(new ItemAttribute
            {
                Key = "project",
                Value = launchPlan.Project
            });
        }

        if (!string.IsNullOrWhiteSpace(launchPlan.System))
        {
            attributes.Add(new ItemAttribute
            {
                Key = "system",
                Value = launchPlan.System
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

        foreach (var attribute in launchPlan.Attributes.Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key)))
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

    private string BuildDescription(AssertionResult assertionResult, ReportPortalLaunchPlan launchPlan)
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
            .AppendLine($"- Team: {launchPlan.Team ?? "<missing-team>"}")
            .AppendLine($"- Project: {launchPlan.Project ?? "<missing-project>"}")
            .AppendLine($"- System: {launchPlan.System}")
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

    private void TryFinishAsFailed(ReportPortalPublishContext launch, string itemUuid)
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
