using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Configurations;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Infrastructure;
using RunnerFileSystemExtensions = QaaS.Runner.Infrastructure.FileSystemExtensions;
using AssertionResult = QaaS.Runner.Assertions.AssertionObjects.AssertionResult;
using AssertionSeverity = QaaS.Runner.Assertions.AssertionObjects.AssertionSeverity;
using ReporterTarget = QaaS.Runner.Assertions.AssertionObjects.ReporterTarget;


namespace QaaS.Runner.Assertions.Reporters;

/// <inheritdoc />
public abstract class BaseReporter : IReporter
{
    protected const string TraceDisplayFalseMessage = "Assertion configured to not display assertion trace",
        QaaSTag = "QaaS",
        RawDataAttachmentType = "application/octet-stream",
        JsonAttachmentType = "application/json",
        XmlAttachmentType = "application/xml",
        YamlAttachmentType = "application/yaml",
        ProtobufAttachmentType = "application/x-protobuff",
        MessagePackAttachmentType = "application/x-msgpack";

    public Context Context = default!;

    public IFileSystem FileSystem = default!;

    public ReporterTarget Target { get; set; }
    public AssertionSeverity Severity { get; set; } = AssertionSeverity.Normal;
    public string Name { get; set; } = string.Empty;
    public string AssertionName { get; set; } = string.Empty;
    public bool SaveSessionData { get; set; }
    public bool SaveLogs { get; set; }
    public bool SaveAttachments { get; set; }
    public bool SaveTemplate { get; set; }
    public bool DisplayTrace { get; set; }
    public DateTime EpochTestSuiteStartTime { get; set; }

    public void WriteTestResults(AssertionResult assertionResult)
    {
        WriteReportCase(BuildReportCase(assertionResult));
    }

    protected abstract void WriteReportCase(ReportCase reportCase);

    protected virtual string AttachmentRootDirectory => string.Empty;

    protected virtual ReportCase BuildReportCase(AssertionResult assertionResult)
    {
        var assertion = assertionResult.Assertion;
        var dataSources =
            $"[{string.Join(", ", assertion.DataSourceList?.Select(dataSource => dataSource.Name).ToArray() ?? [])}]";
        var sessionNames =
            $"[{string.Join(", ", assertion.SessionDataList?.Select(session => session.Name).ToArray() ?? [])}]";
        var testUniqueId = assertion.Name + Context.ExecutionId + Context.CaseName;

        return new ReportCase
        {
            UniqueId = testUniqueId,
            Name = assertion.Name,
            FullName = assertion.Name,
            AssertionType = assertion.AssertionName,
            Status = assertionResult.AssertionStatus,
            Severity = ResolveSeverity(assertion),
            Description =
                $"```yaml\n{assertion.AssertionConfiguration.BuildConfigurationAsYaml()}\n```" +
                (assertionResult.Flaky.IsFlaky
                    ? ArrangeFlakinessReasons(assertionResult.Flaky.FlakinessReasons)
                    : string.Empty),
            Start = EpochTestSuiteStartTime,
            Stop = EpochTestSuiteStartTime.AddMilliseconds(assertionResult.TestDurationMs),
            IsFlaky = assertionResult.Flaky.IsFlaky,
            Links = assertionResult.Links is not null
                ? assertionResult.Links.Select(link => new ReportLink
                {
                    Name = link.Key,
                    Url = link.Value
                }).ToList()
                : [],
            Parameters =
            [
                new ReportParameter
                {
                    Name = "Session Names",
                    Value = sessionNames
                },
                new ReportParameter
                {
                    Name = "Data Sources",
                    Value = dataSources
                }
            ],
            Steps = assertion.SessionDataList?.Select(sessionData => BuildSessionStep(sessionData, assertion)).ToList()
                    ?? [],
            Attachments = BuildAttachmentsForAssertion(assertionResult),
            StatusDetails = BuildStatusDetails(assertionResult)
        };
    }

    protected virtual string GetAttachmentDirectory(string baseAttachmentDirectory, string? extraSubDirectoryName = null)
    {
        var currentAttachmentDirectory = Path.Join(
            BuildAttachmentSegment(baseAttachmentDirectory, nameof(baseAttachmentDirectory)),
            $"{EpochTestSuiteStartTime}");
        var executionAttachmentsDirectory = Context.ExecutionId == null
            ? currentAttachmentDirectory
            : Path.Join(currentAttachmentDirectory,
                BuildAttachmentSegment(Context.ExecutionId, nameof(Context.ExecutionId)));
        var caseAttachmentDirectory = Context.CaseName == null
            ? executionAttachmentsDirectory
            : Path.Join(executionAttachmentsDirectory,
                BuildAttachmentSegment(Context.CaseName, nameof(Context.CaseName)));
        return extraSubDirectoryName == null
            ? caseAttachmentDirectory
            : Path.Join(caseAttachmentDirectory,
                BuildAttachmentSegment(extraSubDirectoryName, nameof(extraSubDirectoryName)));
    }

    protected virtual List<ReportAttachment> BuildAttachmentsForAssertion(AssertionResult assertionResult)
    {
        var attachments = new List<ReportAttachment>();

        if (ShouldSaveAttachments(assertionResult.Assertion))
            attachments.AddRange(BuildAssertionAttachments(assertionResult));
        if (ShouldSaveTemplate(assertionResult.Assertion))
            attachments.Add(BuildConfigurationTemplateAttachment(Context.RootConfiguration));

        attachments.AddRange(BuildCoverageAttachments(assertionResult));
        return attachments;
    }

    protected virtual ReportAttachment BuildConfigurationTemplateAttachment(IConfiguration configuration)
    {
        const string attachmentFile = "template.yaml";
        var renderedTemplate = Context.GetRenderedConfigurationTemplate() ??
                               configuration.BuildConfigurationAsYaml(Infrastructure.Constants.ConfigurationSectionNames);
        return BuildAttachment(
            Encoding.UTF8.GetBytes(renderedTemplate),
            attachmentFile,
            GetAttachmentDirectory("Templates"),
            attachmentFile,
            YamlAttachmentType);
    }

    protected virtual ReportAttachment? BuildSessionLogAttachment(SessionData sessionData)
    {
        const string textAttachmentType = "text/plain";
        var sessionLog = Context.GetSessionLog(sessionData.Name);
        if (string.IsNullOrWhiteSpace(sessionLog))
            return null;

        return BuildAttachment(
            Encoding.UTF8.GetBytes(sessionLog),
            $"{sessionData.Name}.log",
            GetAttachmentDirectory("SessionLogs"),
            "SessionLog",
            textAttachmentType);
    }

    protected virtual ReportAttachment BuildSessionDataAttachment(SessionData sessionData)
    {
        return BuildAttachment(
            SessionDataSerialization.SerializeSessionData(sessionData,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                }),
            $"{sessionData.Name}.json",
            GetAttachmentDirectory("SessionsData"),
            nameof(SessionData),
            JsonAttachmentType);
    }

    protected virtual List<ReportAttachment> BuildAssertionAttachments(AssertionResult assertionResult)
    {
        const string assertionsAttachmentsDirectory = "AssertionsAttachments";
        var specificAssertionAttachmentDirectory = GetAttachmentDirectory(assertionsAttachmentsDirectory,
            assertionResult.Assertion.Name);

        Context.Logger.LogDebug("Saving custom assertion attachments for {AssertionName}",
            assertionResult.Assertion.Name);

        ValidateUniqueAssertionAttachmentPaths(assertionResult);

        var attachments = new List<ReportAttachment>();
        foreach (var assertionAttachment in assertionResult.Assertion.AssertionHook?.AssertionAttachments ?? [])
        {
            var attachmentPath = RunnerFileSystemExtensions.NormalizeRelativePath(assertionAttachment.Path);
            var attachmentFileName = Path.GetFileName(attachmentPath);
            if (string.IsNullOrWhiteSpace(attachmentFileName))
                throw new InvalidOperationException("Assertion attachment path must include a file name.");

            var attachmentDirectoryName = Path.GetDirectoryName(attachmentPath) ?? string.Empty;

            var serializer = SerializerFactory.BuildSerializer(assertionAttachment.SerializationType);
            var assertionData = serializer?.Serialize(assertionAttachment.Data) ??
                                (assertionAttachment.Data != null ? (byte[])assertionAttachment.Data! : []);

            attachments.Add(BuildAttachment(assertionData, attachmentFileName,
                Path.Join(specificAssertionAttachmentDirectory, attachmentDirectoryName), attachmentPath,
                GetAttachmentTypeBySerializationType(assertionAttachment.SerializationType)));
        }

        return attachments;
    }

    protected virtual List<ReportAttachment> BuildCoverageAttachments(AssertionResult assertionResult)
    {
        const string coverageDir = "Coverages";
        var coverageAttachments = new List<ReportAttachment>();
        if (string.IsNullOrWhiteSpace(AttachmentRootDirectory))
            return coverageAttachments;

        var assertionSessionNames = assertionResult.Assertion.SessionDataList.Select(session => session.Name);
        var contextCoverageFiles = new List<string>();
        var fullCoverageDirectory = Path.Combine(AttachmentRootDirectory, coverageDir);
        if (FileSystem.Directory.Exists(fullCoverageDirectory))
            contextCoverageFiles = FileSystem.Directory.EnumerateFiles(fullCoverageDirectory)
                .Select(Path.GetFileName)
                .Where(fileName => fileName != null)
                .ToList()!;
        if (Context.ExecutionId != null)
            contextCoverageFiles = contextCoverageFiles.Where(fileName => fileName.Contains(Context.ExecutionId))
                .ToList();
        if (Context.CaseName != null)
            contextCoverageFiles =
                contextCoverageFiles.Where(fileName => fileName.Contains(Context.CaseName)).ToList();
        foreach (var sessionCoverageFile in assertionSessionNames
                     .SelectMany(sessionName => contextCoverageFiles.Where(fileName => fileName.Contains(sessionName))))
        {
            coverageAttachments.Add(BuildAttachment(
                FileSystem.File.ReadAllBytes(Path.Combine(fullCoverageDirectory, sessionCoverageFile)),
                sessionCoverageFile,
                coverageDir,
                sessionCoverageFile,
                XmlAttachmentType));
        }

        return coverageAttachments;
    }

    protected virtual ReportAttachment BuildAttachment(byte[] data, string fileName, string attachmentDirectory,
        string name, string type)
    {
        var safeAttachmentDirectory = RunnerFileSystemExtensions.NormalizeRelativePath(attachmentDirectory);
        var safeFileName = RunnerFileSystemExtensions.MakeValidFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
            throw new InvalidOperationException("Attachment file name must be set.");

        return new ReportAttachment
        {
            Name = name,
            FileName = safeFileName,
            Directory = safeAttachmentDirectory,
            Source = string.IsNullOrEmpty(safeAttachmentDirectory)
                ? safeFileName
                : Path.Join(safeAttachmentDirectory, safeFileName),
            Type = type,
            Content = data
        };
    }

    protected virtual ReportStatusDetails BuildStatusDetails(AssertionResult assertionResult)
    {
        var displayTrace = ShouldDisplayTrace(assertionResult.Assertion);
        var normalStatusDetails = new ReportStatusDetails
        {
            Message = assertionResult.Assertion.AssertionHook?.AssertionMessage ?? string.Empty,
            Trace = displayTrace
                ? assertionResult.Assertion.AssertionHook?.AssertionTrace ?? string.Empty
                : TraceDisplayFalseMessage,
            Flaky = assertionResult.Flaky.IsFlaky
        };
        var brokenStatusDetails = new ReportStatusDetails
        {
            Message = assertionResult.BrokenAssertionException?.Message ?? string.Empty,
            Trace = displayTrace
                ? assertionResult.BrokenAssertionException?.ToString() ?? string.Empty
                : TraceDisplayFalseMessage,
            Flaky = assertionResult.Flaky.IsFlaky
        };
        return assertionResult.AssertionStatus switch
        {
            AssertionStatus.Passed => normalStatusDetails,
            AssertionStatus.Failed => normalStatusDetails,
            AssertionStatus.Broken => brokenStatusDetails,
            AssertionStatus.Unknown => normalStatusDetails,
            AssertionStatus.Skipped => normalStatusDetails,
            _ => throw new ArgumentOutOfRangeException(nameof(assertionResult.AssertionStatus),
                assertionResult.AssertionStatus, null)
        };
    }

    protected virtual ReportStep BuildSessionStep(SessionData sessionData, AssertionObjects.Assertion assertion)
    {
        var attachments = new List<ReportAttachment>();
        if (ShouldSaveSessionData(assertion))
            attachments.Add(BuildSessionDataAttachment(sessionData));

        var sessionLogAttachment = ShouldSaveLogs(assertion) ? BuildSessionLogAttachment(sessionData) : null;
        if (sessionLogAttachment != null)
            attachments.Add(sessionLogAttachment);

        return new ReportStep
        {
            Name = sessionData.Name,
            Parameters =
            [
                new ReportParameter
                {
                    Name = nameof(sessionData.Inputs),
                    Value =
                        $"[{string.Join(", ", sessionData.Inputs?.Select(input => input.Name).ToArray() ?? [])}]"
                },
                new ReportParameter
                {
                    Name = nameof(sessionData.Outputs),
                    Value =
                        $"[{string.Join(", ", sessionData.Outputs?.Select(output => output.Name).ToArray() ?? [])}]"
                }
            ],
            Status = sessionData.SessionFailures.Any()
                ? AssertionStatus.Failed
                : AssertionStatus.Passed,
            Start = DateTime.SpecifyKind(sessionData.UtcStartTime, DateTimeKind.Utc),
            Stop = DateTime.SpecifyKind(sessionData.UtcEndTime, DateTimeKind.Utc),
            Attachments = attachments,
            Steps = sessionData.SessionFailures.Any()
                ? new List<ReportStep>
                {
                    new()
                    {
                        Name = nameof(sessionData.SessionFailures),
                        Status = AssertionStatus.Failed,
                        Steps = sessionData.SessionFailures.Select(BuildActionFailureStep).ToList()
                    }
                }
                : []
        };
    }

    protected virtual ReportStep BuildActionFailureStep(ActionFailure actionFailure)
    {
        return new ReportStep
        {
            Name = actionFailure.Name,
            Description = actionFailure.ActionType,
            Status = AssertionStatus.Failed,
            Parameters =
            [
                new ReportParameter
                {
                    Name = nameof(actionFailure.Name),
                    Value = actionFailure.Name
                },
                new ReportParameter
                {
                    Name = nameof(actionFailure.ActionType),
                    Value = actionFailure.ActionType
                },
                new ReportParameter
                {
                    Name = nameof(actionFailure.Reason.Description),
                    Value = actionFailure.Reason.Description
                }
            ]
        };
    }

    protected virtual bool ShouldSaveSessionData(AssertionObjects.Assertion assertion) =>
        assertion.SaveSessionData ?? SaveSessionData;

    protected virtual bool ShouldSaveLogs(AssertionObjects.Assertion assertion) =>
        assertion.SaveLogs ?? SaveLogs;

    protected virtual bool ShouldSaveAttachments(AssertionObjects.Assertion assertion) =>
        assertion.SaveAttachments ?? SaveAttachments;

    protected virtual bool ShouldSaveTemplate(AssertionObjects.Assertion assertion) =>
        assertion.SaveTemplate ?? SaveTemplate;

    protected virtual bool ShouldDisplayTrace(AssertionObjects.Assertion assertion) =>
        assertion.DisplayTrace ?? DisplayTrace;

    protected virtual AssertionSeverity ResolveSeverity(AssertionObjects.Assertion assertion) =>
        assertion.Severity ?? Severity;

    protected static string GetAttachmentTypeBySerializationType(SerializationType? serializationType)
    {
        return serializationType switch
        {
            SerializationType.Binary => RawDataAttachmentType,
            SerializationType.ProtobufMessage => ProtobufAttachmentType,
            SerializationType.Json => JsonAttachmentType,
            SerializationType.Yaml => YamlAttachmentType,
            SerializationType.Xml => XmlAttachmentType,
            SerializationType.XmlElement => XmlAttachmentType,
            SerializationType.MessagePack => MessagePackAttachmentType,
            null => RawDataAttachmentType,
            _ => throw new InvalidOperationException($"Unsupported serialization type {serializationType} given")
        };
    }

    protected static string ArrangeFlakinessReasons(
        IEnumerable<KeyValuePair<string, List<ActionFailure>>> flakinessReasons)
    {
        return "\n### Flakiness Reasons" + string.Join("\n",
            flakinessReasons.SelectMany(sessionNameAndFailurePair =>
                sessionNameAndFailurePair.Value.Select(sessionFailure => $@"
- **Session {nameof(SessionData.Name)}:** `{sessionNameAndFailurePair.Key}`
  **{nameof(sessionFailure.Action)}:** `{sessionFailure.Action}`
  **{nameof(sessionFailure.ActionType)}:** `{sessionFailure.ActionType}`
  **{nameof(sessionFailure.Name)}:** `{sessionFailure.Name}`
  **{nameof(sessionFailure.Reason.Message)}:** `{sessionFailure.Reason.Message}`")));
    }

    protected static DateTime ToDateTime(long unixTimeMilliseconds)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(unixTimeMilliseconds).UtcDateTime;
    }

    private static string BuildAttachmentSegment(string? value, string segmentName)
    {
        var safeValue = RunnerFileSystemExtensions.MakeValidDirectoryName(value);
        if (string.IsNullOrWhiteSpace(safeValue))
            throw new InvalidOperationException($"{segmentName} must be set.");

        return safeValue;
    }

    private void ValidateUniqueAssertionAttachmentPaths(AssertionResult assertionResult)
    {
        var assertionAttachmentsPaths = assertionResult.Assertion.AssertionHook?.AssertionAttachments
            .Select(attachment => RunnerFileSystemExtensions.NormalizeRelativePath(attachment.Path));
        var duplicatePaths = assertionAttachmentsPaths?.GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(paths => paths.Count() > 1)
            .Select(item => item.Key)
            .ToList();
        if (duplicatePaths != null && duplicatePaths.Count != 0)
        {
            Context.Logger.LogDebug("Duplicate attachment paths found: {Paths}", string.Join(", ", duplicatePaths));
            throw new InvalidOperationException(
                $"Found duplicate attachment paths for assertion {assertionResult.Assertion.Name}");
        }
    }
}
