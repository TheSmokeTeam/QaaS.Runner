using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Allure.Commons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Configurations;
using QaaS.Framework.Infrastructure;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Infrastructure;
using RunnerFileSystemExtensions = QaaS.Runner.Infrastructure.FileSystemExtensions;
using AssertionResult = QaaS.Runner.Assertions.AssertionObjects.AssertionResult;
using AssertionSeverity = QaaS.Runner.Assertions.AssertionObjects.AssertionSeverity;


namespace QaaS.Runner.Assertions;

/// <inheritdoc />
public class AllureReporter : BaseReporter
{
    public override ReporterKind Kind => ReporterKind.Allure;

    private static readonly IDictionary<AssertionStatus, Status> AssertionStatusToAllureStatusMap =
        new Dictionary<AssertionStatus, Status>
        {
            { AssertionStatus.Passed, Status.passed },
            { AssertionStatus.Failed, Status.failed },
            { AssertionStatus.Broken, Status.broken },
            { AssertionStatus.Unknown, Status.none }
        };

    private static readonly IDictionary<AssertionSeverity, SeverityLevel> AssertionSeverityToAllureSeverityMap =
        new Dictionary<AssertionSeverity, SeverityLevel>
        {
            { AssertionSeverity.Trivial, SeverityLevel.trivial },
            { AssertionSeverity.Minor, SeverityLevel.minor },
            { AssertionSeverity.Normal, SeverityLevel.normal },
            { AssertionSeverity.Critical, SeverityLevel.critical },
            { AssertionSeverity.Blocker, SeverityLevel.blocker }
        };

    private readonly ConcurrentDictionary<string, byte> _alreadySavedAttachments = new();

    /// <summary>
    ///     Saves an attachment as a file in the allure results directory, if it was already saved
    ///     doesn't save it again.
    /// </summary>
    protected virtual void SaveAttachmentIfNotAlreadySaved(byte[] attachmentContent,
        string attachmentDirectory, string attachmentFileName)
    {
        var safeAttachmentDirectory = RunnerFileSystemExtensions.NormalizeRelativePath(attachmentDirectory);
        var safeAttachmentFileName = RunnerFileSystemExtensions.MakeValidFileName(attachmentFileName);
        if (string.IsNullOrWhiteSpace(safeAttachmentFileName))
            throw new InvalidOperationException("Attachment file name must be set.");

        var attachmentUuid = string.Concat(safeAttachmentDirectory, safeAttachmentFileName);
        if (!_alreadySavedAttachments.TryAdd(attachmentUuid, 0))
            return;

        try
        {
            var resultsDirectory = Path.GetFullPath(AllureLifecycle.Instance.ResultsDirectory);
            var attachmentDirectoryPath = RunnerFileSystemExtensions.CombineUnderRoot(resultsDirectory,
                safeAttachmentDirectory);
            if (!FileSystem.Directory.Exists(attachmentDirectoryPath))
                FileSystem.Directory.CreateDirectory(attachmentDirectoryPath);

            var attachmentFullPath = RunnerFileSystemExtensions.CombineUnderRoot(attachmentDirectoryPath,
                safeAttachmentFileName);
            FileSystem.File.WriteAllBytes(attachmentFullPath, attachmentContent);
            Context.Logger.LogDebug("Saved attachment to {AttachmentFullPath}", attachmentFullPath);
        }
        catch
        {
            _alreadySavedAttachments.TryRemove(attachmentUuid, out _);
            throw;
        }
    }

    private static string BuildAttachmentSegment(string? value, string segmentName)
    {
        var safeValue = RunnerFileSystemExtensions.MakeValidDirectoryName(value);
        if (string.IsNullOrWhiteSpace(safeValue))
            throw new InvalidOperationException($"{segmentName} must be set.");

        return safeValue;
    }

    private string GetAttachmentDirectory(string baseAttachmentDirectoryInsideAllureDirectory,
        string? extraSubDirectoryName = null)
    {
        var currentAttachmentDirectory = Path.Join(
            BuildAttachmentSegment(baseAttachmentDirectoryInsideAllureDirectory,
                nameof(baseAttachmentDirectoryInsideAllureDirectory)),
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

    /// <summary>
    ///     Adds an attachment through the active Allure lifecycle item instead of crafting the
    ///     attachment file and metadata manually. Allure's report generator only preserves a
    ///     downloadable attachment source when the lifecycle owns the source creation.
    /// </summary>
    private void AddBytesAttachmentToCurrentItem(byte[] data, string fileName, string name, string type)
    {
        var safeFileName = RunnerFileSystemExtensions.MakeValidFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
            throw new InvalidOperationException("Attachment file name must be set.");

        EnsureResultsDirectoryExists();
        AllureLifecycle.Instance.AddAttachment(name, type, data, ResolveAttachmentExtension(safeFileName, type));
    }

    private void EnsureResultsDirectoryExists()
    {
        var resultsDirectory = Path.GetFullPath(AllureLifecycle.Instance.ResultsDirectory);
        if (!FileSystem.Directory.Exists(resultsDirectory))
            FileSystem.Directory.CreateDirectory(resultsDirectory);
    }

    /// <summary>
    ///     Keeps the legacy folder layout in allure-results for consumers that inspect the raw
    ///     filesystem, while the lifecycle-managed attachment source remains the one referenced by
    ///     the generated Allure result JSON.
    /// </summary>
    private void SaveLegacyAttachmentCopy(byte[] data, string attachmentDirectory, string fileName)
    {
        EnsureResultsDirectoryExists();
        SaveAttachmentIfNotAlreadySaved(data, attachmentDirectory, fileName);
    }

    private static string ResolveAttachmentExtension(string fileName, string attachmentType)
    {
        var extension = Path.GetExtension(fileName);
        if (!string.IsNullOrWhiteSpace(extension))
            return extension;

        return attachmentType switch
        {
            JsonAttachmentType => ".json",
            YamlAttachmentType => ".yaml",
            XmlAttachmentType => ".xml",
            RawDataAttachmentType => ".bin",
            ProtobufAttachmentType => ".proto",
            MessagePackAttachmentType => ".mpack",
            "text/plain" => ".txt",
            _ => ".bin"
        };
    }

    private void AddSessionsDataToCurrentItem(SessionData sessionData)
    {
        var serializedSessionData = SessionDataSerialization.SerializeSessionData(sessionData,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
        Context.Logger.LogDebug("Saving session data for {SessionName} as an Allure attachment", sessionData.Name);
        AddBytesAttachmentToCurrentItem(
            serializedSessionData,
            $"{sessionData.Name}.json",
            nameof(SessionData),
            JsonAttachmentType);
        SaveLegacyAttachmentCopy(serializedSessionData, GetAttachmentDirectory("SessionsData"),
            $"{sessionData.Name}.json");
    }

    private void AddConfigurationTemplateToCurrentItem(IConfiguration configuration)
    {
        const string attachmentFile = "template.yaml";
        Context.Logger.LogDebug("Saving the execution configuration template as an Allure attachment");
        var renderedTemplate = Context.GetRenderedConfigurationTemplate() ??
                               configuration.BuildConfigurationAsYaml(Infrastructure.Constants.ConfigurationSectionNames);
        var renderedTemplateBytes = Encoding.UTF8.GetBytes(renderedTemplate);
        AddBytesAttachmentToCurrentItem(
            renderedTemplateBytes,
            attachmentFile,
            attachmentFile,
            YamlAttachmentType);
        SaveLegacyAttachmentCopy(renderedTemplateBytes, GetAttachmentDirectory("Templates"), attachmentFile);
    }

    private void AddSessionLogToCurrentItem(SessionData sessionData)
    {
        const string textAttachmentType = "text/plain";
        var sessionLog = Context.GetSessionLog(sessionData.Name);
        if (string.IsNullOrWhiteSpace(sessionLog))
            return;

        var sessionLogBytes = Encoding.UTF8.GetBytes(sessionLog);
        Context.Logger.LogDebug("Saving session log for {SessionName} as an Allure attachment", sessionData.Name);
        AddBytesAttachmentToCurrentItem(
            sessionLogBytes,
            $"{sessionData.Name}.log",
            "SessionLog",
            textAttachmentType);
        SaveLegacyAttachmentCopy(sessionLogBytes, GetAttachmentDirectory("SessionLogs"), $"{sessionData.Name}.log");
    }

    private void AddAssertionAttachmentsToCurrentItem(AssertionResult assertionResult)
    {
        const string assertionsAttachmentsDirectory = "AssertionsAttachments";
        var assertionHook = assertionResult.Assertion.AssertionHook;
        if (assertionHook?.AssertionAttachments == null)
            return;

        Context.Logger.LogDebug("Saving custom assertion attachments for {AssertionName}",
            assertionResult.Assertion.Name);

        // validating unique paths
        var assertionAttachmentsPaths = assertionResult.Assertion.AssertionHook?.AssertionAttachments
            .Select(attachment => RunnerFileSystemExtensions.NormalizeRelativePath(attachment.Path));
        var duplicatePaths = assertionAttachmentsPaths?.GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(paths => paths.Count() > 1)
            .Select(item => item.Key).ToList();
        if (duplicatePaths != null && duplicatePaths.Count != 0)
        {
            Context.Logger.LogDebug("Duplicate attachment paths found: {Paths}", string.Join(", ", duplicatePaths));
            throw new InvalidOperationException(
                $"Found duplicate attachment paths for assertion {assertionResult.Assertion.Name}");
        }

        foreach (var assertionAttachment in assertionHook.AssertionAttachments)
        {
            var attachmentPath = RunnerFileSystemExtensions.NormalizeRelativePath(assertionAttachment.Path);
            var attachmentFileName = Path.GetFileName(attachmentPath);
            if (string.IsNullOrWhiteSpace(attachmentFileName))
                throw new InvalidOperationException("Assertion attachment path must include a file name.");

            var attachmentDirectoryName = Path.GetDirectoryName(attachmentPath) ?? string.Empty;

            var serializer = SerializerFactory.BuildSerializer(assertionAttachment.SerializationType);
            var assertionData = serializer?.Serialize(assertionAttachment.Data) ??
                                (assertionAttachment.Data != null ? (byte[])assertionAttachment.Data! : []);
            AddBytesAttachmentToCurrentItem(assertionData,
                string.IsNullOrWhiteSpace(attachmentDirectoryName)
                    ? attachmentFileName
                    : Path.Join(attachmentDirectoryName, attachmentFileName),
                attachmentPath,
                GetAttachmentTypeBySerializationType(assertionAttachment.SerializationType));
            SaveLegacyAttachmentCopy(assertionData,
                Path.Join(GetAttachmentDirectory(assertionsAttachmentsDirectory, assertionResult.Assertion.Name),
                    attachmentDirectoryName),
                attachmentFileName);
        }
    }

    private void AddCoveragesToCurrentItem(AssertionResult assertionResult)
    {
        const string coverageDir = "Coverages";
        var assertionSessionNames = assertionResult.Assertion.SessionDataList.Select(session => session.Name);
        var contextCoverageFiles = new List<string>();
        var fullCoverageDirectory = Path.Combine(AllureLifecycle.Instance.ResultsDirectory, coverageDir);
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
            AddBytesAttachmentToCurrentItem(
                FileSystem.File.ReadAllBytes(Path.Combine(fullCoverageDirectory, sessionCoverageFile)),
                sessionCoverageFile,
                sessionCoverageFile,
                XmlAttachmentType);
        }
    }

    private void AddTestCaseAttachments(AssertionResult assertionResult)
    {
        if (ShouldSaveAttachments(assertionResult.Assertion))
            AddAssertionAttachmentsToCurrentItem(assertionResult);
        if (ShouldSaveTemplate(assertionResult.Assertion))
            AddConfigurationTemplateToCurrentItem(Context.RootConfiguration);
        AddCoveragesToCurrentItem(assertionResult);
    }

    private Attachment SaveSessionsDataToAllure(SessionData sessionData)
    {
        const string sessionAttachmentsDirectory = "SessionsData";
        var sessionDataAttachmentDirectory = GetAttachmentDirectory(sessionAttachmentsDirectory);
        var attachmentFile = $"{sessionData.Name}.json";
        Context.Logger.LogDebug("Saving session data for {SessionName} as an Allure attachment", sessionData.Name);
        return SaveDataToAllure(data: SessionDataSerialization.SerializeSessionData(sessionData,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                }),
            attachmentFile,
            sessionDataAttachmentDirectory,
            nameof(SessionData),
            JsonAttachmentType);
    }

    private Attachment SaveConfigurationTemplateToAllure(IConfiguration configuration)
    {
        const string attachmentFile = "template.yaml";
        const string templateAttachmentsDirectory = "Templates";
        var templateAttachmentsDirectoryFullPath = GetAttachmentDirectory(templateAttachmentsDirectory);
        Context.Logger.LogDebug("Saving the execution configuration template as an Allure attachment");
        var renderedTemplate = Context.GetRenderedConfigurationTemplate() ??
                               configuration.BuildConfigurationAsYaml(Infrastructure.Constants.ConfigurationSectionNames);
        return SaveDataToAllure(
            data: Encoding.UTF8.GetBytes(renderedTemplate),
            fileName: attachmentFile,
            attachmentDirectory: templateAttachmentsDirectoryFullPath,
            name: attachmentFile,
            type: YamlAttachmentType);
    }

    private Attachment? SaveSessionLogToAllure(SessionData sessionData)
    {
        const string sessionLogsAttachmentsDirectory = "SessionLogs";
        const string textAttachmentType = "text/plain";
        var sessionLog = Context.GetSessionLog(sessionData.Name);
        if (string.IsNullOrWhiteSpace(sessionLog))
            return null;

        var sessionLogAttachmentDirectory = GetAttachmentDirectory(sessionLogsAttachmentsDirectory);
        Context.Logger.LogDebug("Saving session log for {SessionName} as an Allure attachment", sessionData.Name);
        return SaveDataToAllure(
            data: Encoding.UTF8.GetBytes(sessionLog),
            fileName: $"{sessionData.Name}.log",
            attachmentDirectory: sessionLogAttachmentDirectory,
            name: "SessionLog",
            type: textAttachmentType);
    }

    private List<Attachment> SaveAssertionAttachmentsToAllure(AssertionResult assertionResult)
    {
        const string assertionsAttachmentsDirectory = "AssertionsAttachments";
        var specificAssertionAttachmentDirectory = GetAttachmentDirectory(assertionsAttachmentsDirectory,
            assertionResult.Assertion.Name);

        var attachments = new List<Attachment>();
        Context.Logger.LogDebug("Saving custom assertion attachments for {AssertionName}",
            assertionResult.Assertion.Name);

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

            attachments.Add(SaveDataToAllure(assertionData, attachmentFileName,
                Path.Join(specificAssertionAttachmentDirectory, attachmentDirectoryName), attachmentPath,
                GetAttachmentTypeBySerializationType(assertionAttachment.SerializationType)));
        }

        return attachments;
    }

    private Attachment SaveDataToAllure(byte[] data, string fileName, string attachmentDirectory, string name,
        string type)
    {
        var safeAttachmentDirectory = RunnerFileSystemExtensions.NormalizeRelativePath(attachmentDirectory);
        var safeFileName = RunnerFileSystemExtensions.MakeValidFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
            throw new InvalidOperationException("Attachment file name must be set.");

        SaveAttachmentIfNotAlreadySaved(data, safeAttachmentDirectory, safeFileName);
        return new Attachment
        {
            name = name,
            source = string.IsNullOrEmpty(safeAttachmentDirectory)
                ? safeFileName
                : Path.Join(safeAttachmentDirectory, safeFileName),
            type = type
        };
    }

    private List<Attachment> GetCoveragesAsAttachments(AssertionResult assertionResult)
    {
        const string coverageDir = "Coverages";
        var coverageAttachments = new List<Attachment>();
        var assertionSessionNames = assertionResult.Assertion.SessionDataList.Select(session => session.Name);
        var contextCoverageFiles = new List<string>();
        var fullCoverageDirectory = Path.Combine(AllureLifecycle.Instance.ResultsDirectory, coverageDir);
        if (Directory.Exists(fullCoverageDirectory))
            contextCoverageFiles = Directory.EnumerateFiles(fullCoverageDirectory)
                .Select(Path.GetFileName)
                .Where(fileName => fileName != null)
                .ToList()!;
        if (Context.ExecutionId != null)
            contextCoverageFiles = contextCoverageFiles.Where(fileName => fileName.Contains(Context.ExecutionId))
                .ToList();
        if (Context.CaseName != null)
            contextCoverageFiles = contextCoverageFiles.Where(fileName => fileName.Contains(Context.CaseName)).ToList();
        foreach (var sessionName in assertionSessionNames)
        {
            var sessionCoverageAttachments = contextCoverageFiles.Where(fileName => fileName.Contains(sessionName))
                .Select(sessionCoverageFile => new Attachment
                    {
                        name = sessionCoverageFile,
                        source = Path.Join(coverageDir, sessionCoverageFile),
                        type = XmlAttachmentType
                    }
                ).ToList();
            coverageAttachments = coverageAttachments.Concat(sessionCoverageAttachments).ToList();
        }

        return coverageAttachments;
    }

    private List<Attachment> GetAttachmentsForAssertion(AssertionResult assertionResult)
    {
        var attachments = new List<Attachment>();

        if (ShouldSaveAttachments(assertionResult.Assertion))
            attachments = attachments.Concat(SaveAssertionAttachmentsToAllure(assertionResult)).ToList();
        if (ShouldSaveTemplate(assertionResult.Assertion))
            attachments = attachments.Append(SaveConfigurationTemplateToAllure(Context.RootConfiguration)).ToList();

        attachments = attachments.Concat(GetCoveragesAsAttachments(assertionResult)).ToList();
        return attachments;
    }

    private List<Label> AddTestCaseLabelsIfIsPartOfTestCase(List<Label> existingLabels)
    {
        if (Context.CaseName == null) return existingLabels;
        return existingLabels.Concat(new[]
        {
            Label.Suite(Context.CaseName),
            Label.Tag(Context.CaseName)
        }).ToList();
    }

    private List<Label> AddExecutionIdLabelsIfIsUnderAnExecutionId(List<Label> existingLabels)
    {
        if (Context.ExecutionId == null) return existingLabels;
        return existingLabels.Concat(new[]
        {
            Label.ParentSuite(Context.ExecutionId),
            Label.Tag(Context.ExecutionId)
        }).ToList();
    }

    private StatusDetails GetStatusDetailsAccordingToStatus(AssertionResult assertionResult)
    {
        var displayTrace = ShouldDisplayTrace(assertionResult.Assertion);
        var normalStatusDetails = new StatusDetails
        {
            message = assertionResult.Assertion.AssertionHook?.AssertionMessage ?? string.Empty,
            trace = displayTrace
                ? assertionResult.Assertion.AssertionHook?.AssertionTrace ?? string.Empty
                : TraceDisplayFalseMessage,
            flaky = assertionResult.Flaky.IsFlaky
        };
        var brokenStatusDetails = new StatusDetails
        {
            message = assertionResult.BrokenAssertionException?.Message ?? string.Empty,
            trace = displayTrace
                ? assertionResult.BrokenAssertionException?.ToString() ?? string.Empty
                : TraceDisplayFalseMessage,
            flaky = assertionResult.Flaky.IsFlaky
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

    public override void WriteTestResults(AssertionResult assertionResult)
    {
        EnsureResultsDirectoryExists();

        // Build test result
        var dataSources =
            $"[{string.Join(", ", assertionResult.Assertion.DataSourceList?.Select(dataSource => dataSource.Name).ToArray() ?? [])}]";
        var sessionNames =
            $"[{string.Join(", ", assertionResult.Assertion.SessionDataList?.Select(session => session.Name).ToArray() ?? [])}]";
        var testUniqueId = assertionResult.Assertion.Name + Context.ExecutionId + Context.CaseName;
        var testResult = new TestResult
        {
            uuid = testUniqueId,
            rerunOf = testUniqueId,
            historyId = testUniqueId,
            name = assertionResult.Assertion.Name,
            fullName = assertionResult.Assertion.Name,
            links = assertionResult.Links is not null
                ? assertionResult.Links.Select(link => new Link
                {
                    name = link.Key,
                    url = link.Value
                }).ToList()
                : Enumerable.Empty<Link>().ToList(),
            status = AssertionStatusToAllureStatusMap[assertionResult.AssertionStatus],
            description =
                $"```yaml\n{assertionResult.Assertion.AssertionConfiguration.BuildConfigurationAsYaml()}\n```" +
                (assertionResult.Flaky.IsFlaky
                    ? ArrangeFlakinessReasons(assertionResult.Flaky.FlakinessReasons)
                    : string.Empty),
            parameters =
            [
                new Parameter
                {
                    name = "Session Names",
                    value = sessionNames
                },

                new Parameter
                {
                    name = "Data Sources",
                    value = dataSources
                }
            ],
            labels = AddExecutionIdLabelsIfIsUnderAnExecutionId(
                AddTestCaseLabelsIfIsPartOfTestCase([
                    Label.TestClass(Context.ExecutionId),
                    Label.TestType(assertionResult.Assertion.AssertionName),
                    Label.Epic(sessionNames),
                    Label.Feature(assertionResult.Assertion.AssertionName),
                    Label.Package(Assembly.GetEntryAssembly()?.GetName().Name ?? QaaSTag),
                    Label.Tag(QaaSTag),
                    Label.Tag(assertionResult.Assertion.AssertionName),
                    Label.Host(),
                    Label.Severity(AssertionSeverityToAllureSeverityMap
                        [ResolveSeverity(assertionResult.Assertion)])
                ])),
            steps = assertionResult.Assertion.SessionDataList?
                .Select(sessionData => CreateSessionStep(sessionData, assertionResult.Assertion))
                .ToList(),
            attachments = GetAttachmentsForAssertion(assertionResult),
            statusDetails = GetStatusDetailsAccordingToStatus(assertionResult)
        };

        // Save test result
        AllureLifecycle.Instance.StartTestCase(testResult);
        AllureLifecycle.Instance.StopTestCase(testUniqueId);
        AllureLifecycle.Instance.UpdateTestCase(testUniqueId, result =>
        {
            // update test duration to be total time of all sessions relevant to assertion + assertion
            result.start = EpochTestSuiteStartTime;
            result.stop = EpochTestSuiteStartTime + assertionResult.TestDurationMs;
        });
        AllureLifecycle.Instance.WriteTestCase(testUniqueId);
    }

    private StepResult CreateSessionStep(SessionData sessionData, AssertionObjects.Assertion assertion)
    {
        var attachments = new List<Attachment>();
        if (ShouldSaveSessionData(assertion))
            attachments.Add(SaveSessionsDataToAllure(sessionData));

        var sessionLogAttachment = ShouldSaveLogs(assertion) ? SaveSessionLogToAllure(sessionData) : null;
        if (sessionLogAttachment != null)
            attachments.Add(sessionLogAttachment);

        return new StepResult
        {
            name = sessionData.Name,

            parameters =
            [
                new Parameter
                {
                    name = nameof(sessionData.Inputs),
                    value =
                        $"[{string.Join(", ", sessionData.Inputs?.Select(input => input.Name).ToArray() ?? [])}]"
                },

                new Parameter
                {
                    name = nameof(sessionData.Outputs),
                    value =
                        $"[{string.Join(", ", sessionData.Outputs?.Select(output => output.Name).ToArray() ?? [])}]"
                }
            ],
            status = sessionData.SessionFailures.Any() ? Status.failed : Status.passed,
            start = new DateTimeOffset(sessionData.UtcStartTime, new TimeSpan(0)).ToUnixTimeMilliseconds(),
            stop = new DateTimeOffset(sessionData.UtcEndTime, new TimeSpan(0)).ToUnixTimeMilliseconds(),
            attachments = attachments.Count == 0 ? null : attachments,
            steps = sessionData.SessionFailures.Any()
                ? new List<StepResult>
                {
                    new()
                    {
                        name = nameof(sessionData.SessionFailures),
                        status = Status.failed,
                        steps = sessionData.SessionFailures.Select(CreateActionFailureStep).ToList()
                    }
                }
                : null
        };
    }

    private static StepResult CreateActionFailureStep(ActionFailure actionFailure)
    {
        return new StepResult
        {
            name = actionFailure.Name,
            description = actionFailure.ActionType,
            status = Status.failed,
            parameters =
            [
                new Parameter
                {
                    name = nameof(actionFailure.Name),
                    value = actionFailure.Name
                },

                new Parameter
                {
                    name = nameof(actionFailure.ActionType),
                    value = actionFailure.ActionType
                },

                new Parameter
                {
                    name = nameof(actionFailure.Reason.Description),
                    value = actionFailure.Reason.Description
                }
            ]
        };
    }

    private void WriteSessionStep(SessionData sessionData, AssertionObjects.Assertion assertion)
    {
        AllureLifecycle.Instance.StartStep(Guid.NewGuid().ToString("N"), CreateSessionStep(sessionData, assertion));
        try
        {
            if (ShouldSaveSessionData(assertion))
                AddSessionsDataToCurrentItem(sessionData);

            if (ShouldSaveLogs(assertion))
                AddSessionLogToCurrentItem(sessionData);
            if (sessionData.SessionFailures.Any())
                WriteSessionFailureSteps(sessionData.SessionFailures);
        }
        finally
        {
            AllureLifecycle.Instance.StopStep(step =>
            {
                step.status = sessionData.SessionFailures.Any() ? Status.failed : Status.passed;
                step.start = new DateTimeOffset(sessionData.UtcStartTime, TimeSpan.Zero).ToUnixTimeMilliseconds();
                step.stop = new DateTimeOffset(sessionData.UtcEndTime, TimeSpan.Zero).ToUnixTimeMilliseconds();
            });
        }
    }

    private void WriteSessionFailureSteps(IReadOnlyCollection<ActionFailure> actionFailures)
    {
        AllureLifecycle.Instance.StartStep(Guid.NewGuid().ToString("N"), new StepResult
        {
            name = nameof(SessionData.SessionFailures),
            status = Status.failed
        });

        try
        {
            foreach (var actionFailure in actionFailures)
            {
                AllureLifecycle.Instance.StartStep(Guid.NewGuid().ToString("N"), CreateActionFailureStep(actionFailure));
                AllureLifecycle.Instance.StopStep(step => step.status = Status.failed);
            }
        }
        finally
        {
            AllureLifecycle.Instance.StopStep(step => step.status = Status.failed);
        }
    }

    private static string ArrangeFlakinessReasons(
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

    private bool ShouldSaveSessionData(AssertionObjects.Assertion assertion) =>
        assertion.SaveSessionData ?? SaveSessionData;

    private bool ShouldSaveLogs(AssertionObjects.Assertion assertion) =>
        assertion.SaveLogs ?? SaveLogs;

    private bool ShouldSaveAttachments(AssertionObjects.Assertion assertion) =>
        assertion.SaveAttachments ?? SaveAttachments;

    private bool ShouldSaveTemplate(AssertionObjects.Assertion assertion) =>
        assertion.SaveTemplate ?? SaveTemplate;

    private bool ShouldDisplayTrace(AssertionObjects.Assertion assertion) =>
        assertion.DisplayTrace ?? DisplayTrace;

    private AssertionSeverity ResolveSeverity(AssertionObjects.Assertion assertion) =>
        assertion.Severity ?? Severity;
}
