using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using QaaS.Framework.Configurations;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;
using ReportPortal.Client.Abstractions.Models;
using ReportPortal.Client.Abstractions.Requests;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using AssertionResult = QaaS.Runner.Assertions.AssertionObjects.AssertionResult;
using AssertionSeverity = QaaS.Runner.Assertions.AssertionObjects.AssertionSeverity;

namespace QaaS.Runner.Assertions;

/// <summary>
/// Publishes QaaS runner assertion results into ReportPortal while preserving the existing Allure writer.
/// Each QaaS assertion result is mapped to one ReportPortal test item plus logs and attachments.
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
    /// Writes one runner-produced assertion result into the shared ReportPortal launch.
    /// The reporter creates the test item, sends outcome/session/template/attachment logs, and then finalizes the item.
    /// </summary>
    /// <param name="assertionResult">The QaaS assertion result produced by the runner pipeline.</param>
    public override void WriteTestResults(AssertionResult assertionResult)
    {
        ArgumentNullException.ThrowIfNull(assertionResult);

        var launch = LaunchManager.EnsureLaunchStartedAsync(Settings, Context.Logger).GetAwaiter().GetResult();
        if (launch is null)
            return;

        var startTimeUtc = GetAssertionStartTime(assertionResult);
        var finishTimeUtc = startTimeUtc.AddMilliseconds(Math.Max(assertionResult.TestDurationMs, 0));
        var itemUuid = launch.Service.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launch.LaunchUuid,
            Name = assertionResult.Assertion.Name,
            Description = BuildDescription(assertionResult),
            StartTime = startTimeUtc,
            Type = TestItemType.Test,
            UniqueId = BuildUniqueId(assertionResult),
            TestCaseId = BuildUniqueId(assertionResult),
            CodeReference = BuildCodeReference(assertionResult),
            Parameters = BuildParameters(assertionResult),
            Attributes = BuildItemAttributes(assertionResult)
        }).GetAwaiter().GetResult().Uuid;

        try
        {
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
                Attributes = BuildItemAttributes(assertionResult)
            }).GetAwaiter().GetResult();
        }
        catch
        {
            TryFinishAsFailed(launch, itemUuid);
            throw;
        }
    }

    private void WriteAssertionOutcomeLog(ReportPortalLaunchContext launch, string itemUuid, AssertionResult assertionResult)
    {
        var logLevel = assertionResult.AssertionStatus switch
        {
            AssertionStatus.Passed => LogLevel.Info,
            AssertionStatus.Skipped => LogLevel.Warning,
            AssertionStatus.Unknown => LogLevel.Warning,
            AssertionStatus.Failed => LogLevel.Error,
            AssertionStatus.Broken => LogLevel.Error,
            _ => LogLevel.Info
        };

        var outcomeText = assertionResult.AssertionStatus switch
        {
            AssertionStatus.Broken => BuildBrokenAssertionText(assertionResult),
            _ => BuildRegularAssertionText(assertionResult)
        };

        CreateLogItem(launch, itemUuid, logLevel, outcomeText, null, null);
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

        CreateLogItem(launch, itemUuid, LogLevel.Info, text, null, null);
    }

    private void WriteSessionLogs(ReportPortalLaunchContext launch, string itemUuid, AssertionResult assertionResult)
    {
        foreach (var sessionData in assertionResult.Assertion.SessionDataList)
        {
            var summary = BuildSessionSummaryText(sessionData);
            if (SaveSessionData)
            {
                CreateLogItem(launch, itemUuid, sessionData.SessionFailures.Any() ? LogLevel.Error : LogLevel.Info,
                    summary,
                    SessionDataSerialization.SerializeSessionData(sessionData,
                        new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                        }),
                    $"{sessionData.Name}.json",
                    JsonAttachmentType);
            }
            else
            {
                CreateLogItem(launch, itemUuid, sessionData.SessionFailures.Any() ? LogLevel.Error : LogLevel.Info,
                    summary, null, null);
            }

            foreach (var actionFailure in sessionData.SessionFailures)
            {
                CreateLogItem(launch, itemUuid, LogLevel.Error, BuildActionFailureText(sessionData, actionFailure),
                    null, null);
            }
        }
    }

    private void WriteTemplateAttachment(ReportPortalLaunchContext launch, string itemUuid)
    {
        if (!SaveTemplate)
            return;

        CreateLogItem(launch, itemUuid, LogLevel.Info, "Execution configuration template.",
            Encoding.UTF8.GetBytes(
                Context.RootConfiguration.BuildConfigurationAsYaml(QaaS.Runner.Infrastructure.Constants
                    .ConfigurationSectionNames)),
            "template.yaml",
            YamlAttachmentType);
    }

    private void WriteAssertionAttachments(ReportPortalLaunchContext launch, string itemUuid, AssertionResult assertionResult)
    {
        if (!SaveAttachments)
            return;

        foreach (var assertionAttachment in assertionResult.Assertion.AssertionHook?.AssertionAttachments ?? [])
        {
            var serializer = SerializerFactory.BuildSerializer(assertionAttachment.SerializationType);
            var serializedData = serializer?.Serialize(assertionAttachment.Data) ??
                                 (assertionAttachment.Data as byte[] ?? []);
            var attachmentName = Path.GetFileName(assertionAttachment.Path);
            if (string.IsNullOrWhiteSpace(attachmentName))
                attachmentName = "attachment.bin";

            CreateLogItem(launch, itemUuid, LogLevel.Info,
                $"Assertion attachment: {assertionAttachment.Path}",
                serializedData,
                attachmentName,
                GetAttachmentTypeBySerializationType(assertionAttachment.SerializationType));
        }
    }

    private void CreateLogItem(ReportPortalLaunchContext launch, string itemUuid, LogLevel level, string text,
        byte[]? attachmentBytes, string? attachmentName, string? attachmentType = null)
    {
        var request = new CreateLogItemRequest
        {
            LaunchUuid = launch.LaunchUuid,
            TestItemUuid = itemUuid,
            Level = level,
            Text = text,
            Time = DateTime.UtcNow
        };

        if (attachmentBytes is not null)
        {
            request.Attach = new LogItemAttach(attachmentType ?? RawDataAttachmentType, attachmentBytes)
            {
                Name = attachmentName ?? Guid.NewGuid().ToString("N")
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
        return new List<KeyValuePair<string, string>>
        {
            new("Session Names",
                $"[{string.Join(", ", assertionResult.Assertion.SessionDataList.Select(session => session.Name))}]"),
            new("Data Sources",
                $"[{string.Join(", ", assertionResult.Assertion.DataSourceList?.Select(dataSource => dataSource.Name) ?? [])}]")
        };
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

        return attributes;
    }

    private string BuildDescription(AssertionResult assertionResult)
    {
        var description = new StringBuilder()
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

    private string BuildUniqueId(AssertionResult assertionResult)
    {
        return string.Join("::",
            new[]
            {
                Context.ExecutionId ?? "runner",
                Context.CaseName ?? "default",
                assertionResult.Assertion.Name
            });
    }

    private string BuildCodeReference(AssertionResult assertionResult)
    {
        return string.Join("/",
            new[]
            {
                Context.ExecutionId ?? "runner",
                Context.CaseName ?? "default",
                assertionResult.Assertion.AssertionName,
                assertionResult.Assertion.Name
            });
    }

    private string BuildRegularAssertionText(AssertionResult assertionResult)
    {
        var message = assertionResult.Assertion.AssertionHook?.AssertionMessage ?? string.Empty;
        var trace = DisplayTrace
            ? assertionResult.Assertion.AssertionHook?.AssertionTrace ?? string.Empty
            : TraceDisplayFalseMessage;

        if (string.IsNullOrWhiteSpace(message) && string.IsNullOrWhiteSpace(trace))
            return $"Assertion completed with status {assertionResult.AssertionStatus}.";

        return string.IsNullOrWhiteSpace(trace)
            ? message
            : $"{message}{Environment.NewLine}{Environment.NewLine}{trace}".Trim();
    }

    private string BuildBrokenAssertionText(AssertionResult assertionResult)
    {
        var trace = DisplayTrace
            ? assertionResult.BrokenAssertionException?.ToString() ?? string.Empty
            : TraceDisplayFalseMessage;

        return $"{assertionResult.BrokenAssertionException?.Message ?? "Broken assertion"}{Environment.NewLine}{Environment.NewLine}{trace}"
            .Trim();
    }

    private static string BuildSessionSummaryText(SessionData sessionData)
    {
        return
            $"Session: {sessionData.Name}{Environment.NewLine}" +
            $"Inputs: [{string.Join(", ", (sessionData.Inputs ?? []).Select(input => input.Name))}]{Environment.NewLine}" +
            $"Outputs: [{string.Join(", ", (sessionData.Outputs ?? []).Select(output => output.Name))}]{Environment.NewLine}" +
            $"Start: {sessionData.UtcStartTime:O}{Environment.NewLine}" +
            $"End: {sessionData.UtcEndTime:O}{Environment.NewLine}" +
            $"Failures: {sessionData.SessionFailures.Count}";
    }

    private static string BuildActionFailureText(SessionData sessionData, ActionFailure actionFailure)
    {
        return
            $"Session `{sessionData.Name}` action failure.{Environment.NewLine}" +
            $"Action: {actionFailure.Action}{Environment.NewLine}" +
            $"Action type: {actionFailure.ActionType}{Environment.NewLine}" +
            $"Name: {actionFailure.Name}{Environment.NewLine}" +
            $"Reason: {actionFailure.Reason.Description}{Environment.NewLine}" +
            $"Message: {actionFailure.Reason.Message}";
    }

    private static string BuildFlakinessText(
        IEnumerable<KeyValuePair<string, List<ActionFailure>>> flakinessReasons)
    {
        var builder = new StringBuilder("Flakiness reasons:");
        foreach (var sessionFailurePair in flakinessReasons)
        {
            foreach (var sessionFailure in sessionFailurePair.Value)
            {
                builder.AppendLine()
                    .Append("- Session ")
                    .Append(sessionFailurePair.Key)
                    .Append(": ")
                    .Append(sessionFailure.Name)
                    .Append(" (")
                    .Append(sessionFailure.ActionType)
                    .Append(") -> ")
                    .Append(sessionFailure.Reason.Message);
            }
        }

        return builder.ToString();
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
