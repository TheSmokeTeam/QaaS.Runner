using System.Collections.Concurrent;
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
/// best-effort: failures are logged as errors and never change the runner exit code.
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
    
    private readonly ConcurrentQueue<AssertionResult> _queuedResults = new();
    public required ReportPortalConfig Config { get; init; }

    /// <summary>
    /// Queues one runner-produced assertion result for final ReportPortal publishing.
    /// </summary>
    public override void WriteTestResults(AssertionResult assertionResult)
    {
        ArgumentNullException.ThrowIfNull(assertionResult);
        _queuedResults.Enqueue(assertionResult);
    }

    internal IReadOnlyList<AssertionResult> GetQueuedResultsSnapshot()
    {
        return _queuedResults.ToArray();
    }

    internal void PublishQueuedResults(ReportPortalPublishContext publishContext,
        IReadOnlyList<ReportPortalAssertionPlan> assertions,
        ILogger logger)
    {
        foreach (var assertion in assertions)
        {
            try
            {
                PublishTestResultCore(publishContext, assertion);
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Could not publish assertion {AssertionName} to ReportPortal for team {TeamName} and system {SystemName}. The run will continue.",
                    assertion.Result.Assertion.Name,
                    publishContext.LaunchPlan.Team,
                    publishContext.LaunchPlan.System);
            }
        }
    }

    private void PublishTestResultCore(ReportPortalPublishContext launch, ReportPortalAssertionPlan assertion)
    {
        var launchPlan = launch.LaunchPlan;
        var assertionResult = assertion.Result;
        var itemAttributes = BuildItemAttributes(assertionResult, launchPlan);
        var stableIdentity = BuildStableReportPortalIdentity(assertionResult,
            launchPlan.Team,
            launchPlan.System);
        var itemUuid = launch.Service.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launch.LaunchUuid,
            Name = assertionResult.Assertion.Name,
            Description = BuildDescription(assertionResult),
            StartTime = assertion.StartTimeUtc,
            Type = TestItemType.Test,
            UniqueId = stableIdentity,
            TestCaseId = stableIdentity,
            CodeReference = BuildCodeReference(stableIdentity),
            Parameters = BuildParameters(assertionResult, launchPlan),
            Attributes = itemAttributes
        }).GetAwaiter().GetResult().Uuid;

        try
        {
            WriteAssertionOutcomeLog(launch, itemUuid, assertionResult, assertion.EndTimeUtc);
            WriteLinksLog(launch, itemUuid, assertionResult, assertion.EndTimeUtc);
            WriteSessionDetails(launch, itemUuid, assertionResult, assertion.EndTimeUtc);
            WriteSessionLogAttachments(launch, itemUuid, assertionResult, assertion.EndTimeUtc);
            WriteTemplateAttachment(launch, itemUuid, assertionResult, assertion.EndTimeUtc);
            WriteAssertionAttachments(launch, itemUuid, assertionResult, assertion.EndTimeUtc);

            launch.Service.TestItem.FinishAsync(itemUuid, new FinishTestItemRequest
            {
                LaunchUuid = launch.LaunchUuid,
                EndTime = assertion.EndTimeUtc,
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

    private void WriteAssertionOutcomeLog(ReportPortalPublishContext launch, string itemUuid,
        AssertionResult assertionResult, DateTime logTimeUtc)
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

        CreateLogItem(launch, itemUuid, logLevel, text.ToString().Trim(), logTimeUtc, null);
    }

    private void WriteLinksLog(ReportPortalPublishContext launch, string itemUuid, AssertionResult assertionResult,
        DateTime logTimeUtc)
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

        CreateLogItem(launch, itemUuid, ReportPortalLogLevel.Info, text, logTimeUtc, null);
    }

    private void WriteSessionDetails(ReportPortalPublishContext launch, string itemUuid, AssertionResult assertionResult,
        DateTime logTimeUtc)
    {
        foreach (var sessionData in assertionResult.Assertion.SessionDataList)
        {
            var actionFailures = ActionFailureNormalizer.Normalize(sessionData.SessionFailures);
            var summary = BuildSessionSummaryText(sessionData);
            var sessionArtifact = BuildSessionArtifact(sessionData, assertionResult.Assertion);
            CreateLogItem(launch, itemUuid,
                actionFailures.Count > 0 ? ReportPortalLogLevel.Error : ReportPortalLogLevel.Info,
                summary,
                logTimeUtc,
                sessionArtifact);

            foreach (var actionFailure in actionFailures)
            {
                CreateLogItem(launch, itemUuid, ReportPortalLogLevel.Error,
                    BuildActionFailureText(sessionData, actionFailure), logTimeUtc, null);
            }
        }
    }

    private void WriteSessionLogAttachments(ReportPortalPublishContext launch, string itemUuid,
        AssertionResult assertionResult, DateTime logTimeUtc)
    {
        foreach (var sessionData in assertionResult.Assertion.SessionDataList)
        {
            var sessionLogArtifact = BuildSessionLogArtifact(sessionData, assertionResult.Assertion);
            if (sessionLogArtifact is null)
                continue;

            CreateLogItem(launch, itemUuid, ReportPortalLogLevel.Info,
                $"Session log: {sessionData.Name}",
                logTimeUtc,
                sessionLogArtifact);
        }
    }

    private void WriteTemplateAttachment(ReportPortalPublishContext launch, string itemUuid,
        AssertionResult assertionResult, DateTime logTimeUtc)
    {
        var templateArtifact = BuildTemplateArtifact(assertionResult.Assertion);
        if (templateArtifact is null)
            return;

        CreateLogItem(launch, itemUuid, ReportPortalLogLevel.Info, "Execution configuration template.",
            logTimeUtc, templateArtifact);
    }

    private void WriteAssertionAttachments(ReportPortalPublishContext launch, string itemUuid,
        AssertionResult assertionResult, DateTime logTimeUtc)
    {
        foreach (var artifact in BuildAssertionArtifacts(assertionResult))
        {
            CreateLogItem(launch, itemUuid, ReportPortalLogLevel.Info,
                $"Assertion attachment: {artifact.RelativePath}",
                logTimeUtc,
                artifact);
        }
    }

    private void CreateLogItem(ReportPortalPublishContext launch, string itemUuid, ReportPortalLogLevel level,
        string text,
        DateTime timeUtc,
        ReportArtifact? artifact)
    {
        var request = new CreateLogItemRequest
        {
            LaunchUuid = launch.LaunchUuid,
            TestItemUuid = itemUuid,
            Level = level,
            Text = text,
            Time = timeUtc
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

    private IList<KeyValuePair<string, string>> BuildParameters(AssertionResult assertionResult,
        ReportPortalLaunchPlan launchPlan)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("Session Names",
                $"[{string.Join(", ", assertionResult.Assertion.SessionDataList.Select(session => session.Name))}]"),
            new("Data Sources",
                $"[{string.Join(", ", assertionResult.Assertion.DataSourceList?.Select(dataSource => dataSource.Name) ?? [])}]"),
            new("Team", launchPlan.Team),
            new("System", launchPlan.System)
        };

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
                Key = "assertion",
                Value = assertionResult.Assertion.AssertionName
            },
            new()
            {
                Key = "severity",
                Value = AssertionSeverityToAttributeValueMap[ResolveSeverity(assertionResult.Assertion)]
            },
            new()
            {
                Key = "flaky",
                Value = assertionResult.Flaky.IsFlaky ? "true" : "false"
            },
            new()
            {
                Key = "team",
                Value = launchPlan.Team
            },
            new()
            {
                Key = "system",
                Value = launchPlan.System
            }
        };

        var sessionNames = assertionResult.Assertion.SessionDataList
            .Select(session => session.Name)
            .Where(sessionName => !string.IsNullOrWhiteSpace(sessionName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(sessionName => sessionName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        attributes.Add(new ItemAttribute
        {
            Key = "sessionCount",
            Value = sessionNames.Length.ToString()
        });

        if (sessionNames.Length > 0)
        {
            attributes.Add(new ItemAttribute
            {
                Key = "sessions",
                Value = string.Join(", ", sessionNames)
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
        var description = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(assertionTextDetails.Message))
        {
            description.AppendLine("Assertion message:")
                .AppendLine(assertionTextDetails.Message.Trim());
        }

        description.AppendLine("<br />")
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
