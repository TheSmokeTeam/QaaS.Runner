using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using QaaS.Framework.Configurations;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Infrastructure;
using ReportPortal.Client.Abstractions.Models;
using ReportPortal.Client.Abstractions.Requests;
using Status = ReportPortal.Client.Abstractions.Models.Status;

namespace QaaS.Runner.Assertions;

/// <summary>
/// Writes assertion results to ReportPortal.
/// </summary>
public class ReportPortalReporter : BaseReporter
{
    public override ReporterKind Kind => ReporterKind.ReportPortal;

    private static readonly IDictionary<AssertionStatus, Status> AssertionStatusToReportPortalStatusMap =
        new Dictionary<AssertionStatus, Status>
        {
            { AssertionStatus.Passed, Status.Passed },
            { AssertionStatus.Failed, Status.Failed },
            { AssertionStatus.Broken, Status.Failed },
            { AssertionStatus.Skipped, Status.Skipped },
            { AssertionStatus.Unknown, Status.Failed }
        };

    public override void WriteTestResults(AssertionResult assertionResult)
    {
        var launchAccessor = GetLaunchAccessor();
        if (!launchAccessor.IsActive || string.IsNullOrWhiteSpace(launchAccessor.LaunchUuid))
            throw new InvalidOperationException("ReportPortal launch is not active.");

        var testItemDescription = BuildDescription(assertionResult);
        var startTime = EpochToUtcDateTimeOffset(EpochTestSuiteStartTime);
        var endTime = EpochToUtcDateTimeOffset(EpochTestSuiteStartTime + assertionResult.TestDurationMs);
        var testItemUuid = launchAccessor.Client.TestItem.StartAsync(new StartTestItemRequest
        {
            LaunchUuid = launchAccessor.LaunchUuid,
            Name = assertionResult.Assertion.Name,
            Description = testItemDescription,
            StartTime = startTime.DateTime,
            Type = TestItemType.Test,
            UniqueId = BuildUniqueId(assertionResult),
            TestCaseId = BuildUniqueId(assertionResult),
            CodeReference = assertionResult.Assertion.AssertionName,
            HasStats = true,
            Attributes = BuildAttributes(assertionResult).ToList()
        }).GetAwaiter().GetResult().Uuid;

        WriteLogs(launchAccessor, testItemUuid, assertionResult, startTime);

        launchAccessor.Client.TestItem.FinishAsync(testItemUuid, new FinishTestItemRequest
        {
            LaunchUuid = launchAccessor.LaunchUuid,
            EndTime = endTime.DateTime,
            Status = AssertionStatusToReportPortalStatusMap[assertionResult.AssertionStatus],
            Description = testItemDescription,
            Attributes = BuildAttributes(assertionResult).ToList()
        }).GetAwaiter().GetResult();
    }

    private IReportPortalLaunchAccessor GetLaunchAccessor()
    {
        if (Context is not InternalContext internalContext)
            throw new InvalidOperationException("ReportPortal reporter requires an internal execution context.");

        if (!internalContext.InternalGlobalDict.TryGetValue(ReportingContextKeys.ReportPortalLaunchAccessor,
                out var storedAccessor) ||
            storedAccessor is not IReportPortalLaunchAccessor launchAccessor)
        {
            throw new InvalidOperationException("ReportPortal reporter could not resolve the shared launch accessor.");
        }

        if (!launchAccessor.IsEnabled)
            throw new InvalidOperationException("ReportPortal launch accessor is disabled for this run.");

        return launchAccessor;
    }

    private static DateTimeOffset EpochToUtcDateTimeOffset(long unixTimeMilliseconds)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(unixTimeMilliseconds);
    }

    private string BuildUniqueId(AssertionResult assertionResult)
    {
        return string.Join("::",
            new[]
            {
                Context.ExecutionId ?? "no-execution",
                Context.CaseName ?? "no-case",
                assertionResult.Assertion.Name
            });
    }

    private IEnumerable<ItemAttribute> BuildAttributes(AssertionResult assertionResult)
    {
        var attributes = new List<ItemAttribute>
        {
            new() { Key = "backend", Value = Kind.ToString() },
            new() { Key = "assertion", Value = assertionResult.Assertion.AssertionName },
            new() { Key = "severity", Value = ResolveSeverity(assertionResult.Assertion).ToString() },
            new() { Key = "flaky", Value = assertionResult.Flaky.IsFlaky.ToString() }
        };

        if (!string.IsNullOrWhiteSpace(Context.ExecutionId))
            attributes.Add(new ItemAttribute { Key = "executionId", Value = Context.ExecutionId });

        if (!string.IsNullOrWhiteSpace(Context.CaseName))
            attributes.Add(new ItemAttribute { Key = "caseName", Value = Context.CaseName });

        foreach (var sessionName in assertionResult.Assertion.SessionDataList.Select(session => session.Name).Distinct())
            attributes.Add(new ItemAttribute { Key = "session", Value = sessionName });

        var metaData = ((InternalContext)Context).GetMetaDataOrDefault();
        if (!string.IsNullOrWhiteSpace(metaData.Team))
            attributes.Add(new ItemAttribute { Key = "team", Value = metaData.Team });
        if (!string.IsNullOrWhiteSpace(metaData.System))
            attributes.Add(new ItemAttribute { Key = "system", Value = metaData.System });

        return attributes;
    }

    private string BuildDescription(AssertionResult assertionResult)
    {
        var builder = new StringBuilder()
            .Append("```yaml\n")
            .Append(assertionResult.Assertion.AssertionConfiguration.BuildConfigurationAsYaml())
            .Append("\n```");

        if (assertionResult.AssertionStatus == AssertionStatus.Broken &&
            assertionResult.BrokenAssertionException != null)
        {
            builder.Append("\n\nBroken assertion:\n")
                .Append(assertionResult.BrokenAssertionException);
        }

        if (assertionResult.Flaky.IsFlaky)
            builder.Append(ArrangeFlakinessReasons(assertionResult.Flaky.FlakinessReasons));

        return builder.ToString();
    }

    private void WriteLogs(IReportPortalLaunchAccessor launchAccessor, string testItemUuid, AssertionResult assertionResult,
        DateTimeOffset baseTime)
    {
        WriteTextLog(launchAccessor, testItemUuid, baseTime, LogLevel.Info,
            $"Assertion '{assertionResult.Assertion.Name}' completed with status {assertionResult.AssertionStatus}.");

        foreach (var sessionData in assertionResult.Assertion.SessionDataList)
            WriteSessionLogs(launchAccessor, testItemUuid, sessionData, assertionResult.Assertion);

        WriteTemplateAttachmentIfNeeded(launchAccessor, testItemUuid, assertionResult);
        WriteAssertionAttachmentsIfNeeded(launchAccessor, testItemUuid, assertionResult);
        WriteCoverageAttachments(launchAccessor, testItemUuid, assertionResult);
    }

    private void WriteSessionLogs(IReportPortalLaunchAccessor launchAccessor, string testItemUuid, SessionData sessionData,
        AssertionObjects.Assertion assertion)
    {
        WriteTextLog(launchAccessor, testItemUuid,
            new DateTimeOffset(sessionData.UtcStartTime, TimeSpan.Zero),
            sessionData.SessionFailures.Any() ? LogLevel.Error : LogLevel.Info,
            $"Session {sessionData.Name} Inputs=[{string.Join(", ", sessionData.Inputs?.Select(input => input.Name) ?? [])}] Outputs=[{string.Join(", ", sessionData.Outputs?.Select(output => output.Name) ?? [])}]");

        if (ShouldSaveSessionData(assertion))
        {
            var serializedSessionData = SessionDataSerialization.SerializeSessionData(sessionData,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });
            WriteAttachmentLog(launchAccessor, testItemUuid, new DateTimeOffset(sessionData.UtcEndTime, TimeSpan.Zero),
                LogLevel.Info, $"Session data for {sessionData.Name}", $"{sessionData.Name}.json", JsonAttachmentType,
                serializedSessionData);
        }

        if (ShouldSaveLogs(assertion))
        {
            var sessionLog = Context.GetSessionLog(sessionData.Name);
            if (!string.IsNullOrWhiteSpace(sessionLog))
            {
                WriteAttachmentLog(launchAccessor, testItemUuid, new DateTimeOffset(sessionData.UtcEndTime, TimeSpan.Zero),
                    LogLevel.Info, $"Session log for {sessionData.Name}", $"{sessionData.Name}.log", "text/plain",
                    Encoding.UTF8.GetBytes(sessionLog));
            }
        }

        foreach (var actionFailure in sessionData.SessionFailures)
        {
            WriteTextLog(launchAccessor, testItemUuid, new DateTimeOffset(sessionData.UtcEndTime, TimeSpan.Zero),
                LogLevel.Error,
                $"Session '{sessionData.Name}' action failure. Action={actionFailure.Action} Type={actionFailure.ActionType} Name={actionFailure.Name} Reason={actionFailure.Reason.Message}");
        }
    }

    private bool ShouldSaveSessionData(AssertionObjects.Assertion assertion) =>
        assertion.SaveSessionData ?? SaveSessionData;

    private bool ShouldSaveLogs(AssertionObjects.Assertion assertion) =>
        assertion.SaveLogs ?? SaveLogs;

    private bool ShouldSaveAttachments(AssertionObjects.Assertion assertion) =>
        assertion.SaveAttachments ?? SaveAttachments;

    private bool ShouldSaveTemplate(AssertionObjects.Assertion assertion) =>
        assertion.SaveTemplate ?? SaveTemplate;

    private AssertionSeverity ResolveSeverity(AssertionObjects.Assertion assertion) =>
        assertion.Severity ?? Severity;

    private void WriteTemplateAttachmentIfNeeded(IReportPortalLaunchAccessor launchAccessor, string testItemUuid,
        AssertionResult assertionResult)
    {
        if (!ShouldSaveTemplate(assertionResult.Assertion))
            return;

        var renderedTemplate = Context.GetRenderedConfigurationTemplate() ??
                               Context.RootConfiguration.BuildConfigurationAsYaml(
                                   Infrastructure.Constants.ConfigurationSectionNames);
        WriteAttachmentLog(launchAccessor, testItemUuid, DateTime.UtcNow, LogLevel.Info,
            "Execution configuration template", "template.yaml", YamlAttachmentType,
            Encoding.UTF8.GetBytes(renderedTemplate));
    }

    private void WriteAssertionAttachmentsIfNeeded(IReportPortalLaunchAccessor launchAccessor, string testItemUuid,
        AssertionResult assertionResult)
    {
        if (!ShouldSaveAttachments(assertionResult.Assertion))
            return;

        foreach (var assertionAttachment in assertionResult.Assertion.AssertionHook?.AssertionAttachments ?? [])
        {
            var serializer = SerializerFactory.BuildSerializer(assertionAttachment.SerializationType);
            var attachmentData = serializer?.Serialize(assertionAttachment.Data) ??
                                 (assertionAttachment.Data != null ? (byte[])assertionAttachment.Data : []);
            var attachmentPath = QaaS.Runner.Infrastructure.FileSystemExtensions.NormalizeRelativePath(
                assertionAttachment.Path);
            var fileName = Path.GetFileName(attachmentPath);
            if (string.IsNullOrWhiteSpace(fileName))
                throw new InvalidOperationException("Assertion attachment path must include a file name.");

            WriteAttachmentLog(launchAccessor, testItemUuid, DateTime.UtcNow, LogLevel.Info,
                $"Assertion attachment {attachmentPath}", fileName,
                GetAttachmentTypeBySerializationType(assertionAttachment.SerializationType), attachmentData);
        }
    }

    private void WriteCoverageAttachments(IReportPortalLaunchAccessor launchAccessor, string testItemUuid,
        AssertionResult assertionResult)
    {
        const string coverageDir = "Coverages";
        var fullCoverageDirectory = Path.Combine("allure-results", coverageDir);
        if (!FileSystem.Directory.Exists(fullCoverageDirectory))
            return;

        var assertionSessionNames = assertionResult.Assertion.SessionDataList.Select(session => session.Name).ToHashSet();
        foreach (var coverageFile in FileSystem.Directory.EnumerateFiles(fullCoverageDirectory))
        {
            var fileName = Path.GetFileName(coverageFile);
            if (string.IsNullOrWhiteSpace(fileName))
                continue;

            if (!assertionSessionNames.Any(fileName.Contains))
                continue;

            if (Context.ExecutionId != null && !fileName.Contains(Context.ExecutionId))
                continue;

            if (Context.CaseName != null && !fileName.Contains(Context.CaseName))
                continue;

            WriteAttachmentLog(launchAccessor, testItemUuid, DateTimeOffset.UtcNow, LogLevel.Info,
                $"Coverage {fileName}", fileName, XmlAttachmentType, FileSystem.File.ReadAllBytes(coverageFile));
        }
    }

    private void WriteTextLog(IReportPortalLaunchAccessor launchAccessor, string testItemUuid, DateTimeOffset time,
        LogLevel level, string text)
    {
        launchAccessor.Client.LogItem.CreateAsync(new CreateLogItemRequest
        {
            LaunchUuid = launchAccessor.LaunchUuid,
            TestItemUuid = testItemUuid,
            Time = time.DateTime,
            Level = level,
            Text = text
        }).GetAwaiter().GetResult();
    }

    private void WriteAttachmentLog(IReportPortalLaunchAccessor launchAccessor, string testItemUuid, DateTimeOffset time,
        LogLevel level, string text, string fileName, string mimeType, byte[] data)
    {
        var attachment = new LogItemAttach(mimeType, data)
        {
            Name = fileName
        };

        launchAccessor.Client.LogItem.CreateAsync(new CreateLogItemRequest
        {
            LaunchUuid = launchAccessor.LaunchUuid,
            TestItemUuid = testItemUuid,
            Time = time.DateTime,
            Level = level,
            Text = text,
            Attach = attachment
        }).GetAwaiter().GetResult();
    }

    private static string ArrangeFlakinessReasons(
        IEnumerable<KeyValuePair<string, List<ActionFailure>>> flakinessReasons)
    {
        return "\n\nFlakiness Reasons" + string.Join("\n",
            flakinessReasons.SelectMany(sessionNameAndFailurePair =>
                sessionNameAndFailurePair.Value.Select(sessionFailure =>
                    $"\n- Session={sessionNameAndFailurePair.Key} Action={sessionFailure.Action} Type={sessionFailure.ActionType} Name={sessionFailure.Name} Reason={sessionFailure.Reason.Message}")));
    }
}
