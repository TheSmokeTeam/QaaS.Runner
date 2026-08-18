using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Allure.Commons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Configurations;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Assertions;
using QaaS.Runner.Infrastructure;
using AssertionResult = QaaS.Runner.Assertions.AssertionObjects.AssertionResult;
using AssertionSeverity = QaaS.Runner.Assertions.AssertionObjects.AssertionSeverity;
using RunnerFileSystemExtensions = QaaS.Runner.Infrastructure.FileSystemExtensions;

namespace QaaS.Runner.Assertions.Reporters.Allure;

/// <inheritdoc />
public class AllureReporter : BaseReporter
{
    private static readonly IDictionary<AssertionStatus, Status> AssertionStatusToAllureStatusMap =
        new Dictionary<AssertionStatus, Status>
        {
            { AssertionStatus.Passed, Status.passed },
            { AssertionStatus.Failed, Status.failed },
            { AssertionStatus.Broken, Status.broken },
            { AssertionStatus.Unknown, Status.none },
            { AssertionStatus.Skipped, Status.skipped },
        };

    private static readonly IDictionary<
        AssertionSeverity,
        SeverityLevel
    > AssertionSeverityToAllureSeverityMap = new Dictionary<AssertionSeverity, SeverityLevel>
    {
        { AssertionSeverity.Trivial, SeverityLevel.trivial },
        { AssertionSeverity.Minor, SeverityLevel.minor },
        { AssertionSeverity.Normal, SeverityLevel.normal },
        { AssertionSeverity.Critical, SeverityLevel.critical },
        { AssertionSeverity.Blocker, SeverityLevel.blocker },
    };

    private readonly ConcurrentDictionary<string, byte> _alreadySavedAttachments = new();
    private readonly ConcurrentDictionary<string, Lazy<string>> _savedAttachmentSources = new();

    /// <summary>
    /// Saves an attachment under its logical results-directory path unless that path was already
    /// saved by this reporter.
    /// </summary>
    /// <remarks>
    /// This protected extension point is retained for compatibility with existing reporter
    /// subclasses and raw-results consumers. Allure result JSON references the additional flat,
    /// portable copy created by the reporter.
    /// </remarks>
    protected virtual void SaveAttachmentIfNotAlreadySaved(
        byte[] attachmentContent,
        string attachmentDirectory,
        string attachmentFileName
    )
    {
        var safeAttachmentDirectory = RunnerFileSystemExtensions.NormalizeRelativePath(
            attachmentDirectory
        );
        var safeAttachmentFileName = RunnerFileSystemExtensions.MakeValidFileName(
            attachmentFileName
        );
        if (string.IsNullOrWhiteSpace(safeAttachmentFileName))
            throw new InvalidOperationException("Attachment file name must be set.");

        var attachmentKey = Path.Join(safeAttachmentDirectory, safeAttachmentFileName);
        if (!_alreadySavedAttachments.TryAdd(attachmentKey, 0))
            return;

        try
        {
            var resultsDirectory = Path.GetFullPath(AllureLifecycle.Instance.ResultsDirectory);
            var attachmentDirectoryPath = RunnerFileSystemExtensions.CombineUnderRoot(
                resultsDirectory,
                safeAttachmentDirectory
            );
            if (!FileSystem.Directory.Exists(attachmentDirectoryPath))
                FileSystem.Directory.CreateDirectory(attachmentDirectoryPath);

            var attachmentFullPath = RunnerFileSystemExtensions.CombineUnderRoot(
                attachmentDirectoryPath,
                safeAttachmentFileName
            );
            FileSystem.File.WriteAllBytes(attachmentFullPath, attachmentContent);
            Context.Logger.LogDebug(
                "Saved compatibility attachment to {AttachmentFullPath}",
                attachmentFullPath
            );
        }
        catch
        {
            _alreadySavedAttachments.TryRemove(attachmentKey, out _);
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

    private string GetAttachmentDirectory(
        string baseAttachmentDirectoryInsideAllureDirectory,
        string? extraSubDirectoryName = null
    )
    {
        var currentAttachmentDirectory = Path.Join(
            BuildAttachmentSegment(
                baseAttachmentDirectoryInsideAllureDirectory,
                nameof(baseAttachmentDirectoryInsideAllureDirectory)
            ),
            $"{EpochTestSuiteStartTime}"
        );
        var executionAttachmentsDirectory =
            Context.ExecutionId == null
                ? currentAttachmentDirectory
                : Path.Join(
                    currentAttachmentDirectory,
                    BuildAttachmentSegment(Context.ExecutionId, nameof(Context.ExecutionId))
                );
        var caseAttachmentDirectory =
            Context.CaseName == null
                ? executionAttachmentsDirectory
                : Path.Join(
                    executionAttachmentsDirectory,
                    BuildAttachmentSegment(Context.CaseName, nameof(Context.CaseName))
                );
        return extraSubDirectoryName == null
            ? caseAttachmentDirectory
            : Path.Join(
                caseAttachmentDirectory,
                BuildAttachmentSegment(extraSubDirectoryName, nameof(extraSubDirectoryName))
            );
    }

    private void EnsureResultsDirectoryExists()
    {
        var resultsDirectory = Path.GetFullPath(AllureLifecycle.Instance.ResultsDirectory);
        if (!FileSystem.Directory.Exists(resultsDirectory))
            FileSystem.Directory.CreateDirectory(resultsDirectory);
    }

    private static string ResolveAttachmentExtension(string fileName, string attachmentType)
    {
        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrWhiteSpace(extension) && IsAllureCompatibleExtension(extension)
            ? extension
            : GetDefaultAttachmentExtension(attachmentType);
    }

    private static bool IsAllureCompatibleExtension(string extension)
    {
        const int allureAttachmentSourceMaxLength = 100;
        var maxExtensionLength =
            allureAttachmentSourceMaxLength
            - Guid.Empty.ToString("N").Length
            - AllureConstants.ATTACHMENT_FILE_SUFFIX.Length;
        return extension.Length <= maxExtensionLength
            && extension.StartsWith('.')
            && extension.All(character =>
                char.IsAsciiLetterOrDigit(character) || "._-".Contains(character)
            );
    }

    private static string GetDefaultAttachmentExtension(string attachmentType)
    {
        return attachmentType switch
        {
            JsonAttachmentType => ".json",
            YamlAttachmentType => ".yaml",
            XmlAttachmentType => ".xml",
            RawDataAttachmentType => ".bin",
            ProtobufAttachmentType => ".proto",
            MessagePackAttachmentType => ".mpack",
            "text/plain" => ".txt",
            _ => ".bin",
        };
    }

    private string SaveAttachmentIfNotAlreadySaved(
        byte[] attachmentContent,
        string attachmentDirectory,
        string attachmentFileName,
        string attachmentType
    )
    {
        var safeAttachmentDirectory = RunnerFileSystemExtensions.NormalizeRelativePath(
            attachmentDirectory
        );
        var safeAttachmentFileName = RunnerFileSystemExtensions.MakeValidFileName(
            attachmentFileName
        );
        if (string.IsNullOrWhiteSpace(safeAttachmentFileName))
            throw new InvalidOperationException("Attachment file name must be set.");

        var attachmentKey = Path.Join(safeAttachmentDirectory, safeAttachmentFileName);
        var candidateAttachment = new Lazy<string>(
            () =>
            {
                EnsureResultsDirectoryExists();
                var source =
                    $"{Guid.NewGuid():N}{AllureConstants.ATTACHMENT_FILE_SUFFIX}{ResolveAttachmentExtension(safeAttachmentFileName, attachmentType)}";
                SaveAttachmentIfNotAlreadySaved(
                    attachmentContent,
                    safeAttachmentDirectory,
                    safeAttachmentFileName
                );
                var resultsDirectory = Path.GetFullPath(AllureLifecycle.Instance.ResultsDirectory);
                var attachmentFullPath = RunnerFileSystemExtensions.CombineUnderRoot(
                    resultsDirectory,
                    source
                );
                FileSystem.File.WriteAllBytes(attachmentFullPath, attachmentContent);
                Context.Logger.LogDebug(
                    "Saved attachment to {AttachmentFullPath}",
                    attachmentFullPath
                );
                return source;
            },
            System.Threading.LazyThreadSafetyMode.ExecutionAndPublication
        );
        var savedAttachment = _savedAttachmentSources.GetOrAdd(attachmentKey, candidateAttachment);

        try
        {
            return savedAttachment.Value;
        }
        catch
        {
            if (
                _savedAttachmentSources.TryGetValue(attachmentKey, out var currentAttachment)
                && ReferenceEquals(savedAttachment, currentAttachment)
            )
                _savedAttachmentSources.TryRemove(attachmentKey, out _);
            throw;
        }
    }

    private Attachment SaveSessionsDataToAllure(SessionData sessionData)
    {
        const string sessionAttachmentsDirectory = "SessionsData";
        Context.Logger.LogDebug(
            "Saving session data for {SessionName} as an Allure attachment",
            sessionData.Name
        );
        var normalizedSessionData = sessionData with
        {
            SessionFailures = ActionFailureNormalizer.Normalize(sessionData.SessionFailures),
        };
        return SaveDataToAllure(
            SessionDataSerialization.SerializeSessionData(
                normalizedSessionData,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                }
            ),
            $"{sessionData.Name}.json",
            GetAttachmentDirectory(sessionAttachmentsDirectory),
            nameof(SessionData),
            JsonAttachmentType
        );
    }

    private Attachment SaveConfigurationTemplateToAllure(IConfiguration configuration)
    {
        const string attachmentFile = "template.yaml";
        const string templateAttachmentsDirectory = "Templates";
        Context.Logger.LogDebug(
            "Saving the execution configuration template as an Allure attachment"
        );
        var renderedTemplate =
            Context.GetRenderedConfigurationTemplate()
            ?? configuration.BuildConfigurationAsYaml(
                Infrastructure.Constants.ConfigurationSectionNames
            );
        return SaveDataToAllure(
            Encoding.UTF8.GetBytes(renderedTemplate),
            attachmentFile,
            GetAttachmentDirectory(templateAttachmentsDirectory),
            attachmentFile,
            YamlAttachmentType
        );
    }

    private Attachment? SaveSessionLogToAllure(SessionData sessionData)
    {
        const string sessionLogsAttachmentsDirectory = "SessionLogs";
        const string textAttachmentType = "text/plain";
        var sessionLog = Context.GetSessionLog(sessionData.Name);
        if (string.IsNullOrWhiteSpace(sessionLog))
            return null;

        Context.Logger.LogDebug(
            "Saving session log for {SessionName} as an Allure attachment",
            sessionData.Name
        );
        return SaveDataToAllure(
            Encoding.UTF8.GetBytes(sessionLog),
            $"{sessionData.Name}.log",
            GetAttachmentDirectory(sessionLogsAttachmentsDirectory),
            "SessionLog",
            textAttachmentType
        );
    }

    private List<Attachment> SaveAssertionAttachmentsToAllure(AssertionResult assertionResult)
    {
        const string assertionsAttachmentsDirectory = "AssertionsAttachments";
        var specificAssertionAttachmentDirectory = GetAttachmentDirectory(
            assertionsAttachmentsDirectory,
            assertionResult.Assertion.Name
        );
        Context.Logger.LogDebug(
            "Saving custom assertion attachments for {AssertionName}",
            assertionResult.Assertion.Name
        );
        var attachments = new List<Attachment>();
        foreach (
            var assertionAttachment in assertionResult.Assertion.AssertionHook?.AssertionAttachments
                ?? []
        )
        {
            var attachmentPath = RunnerFileSystemExtensions.NormalizeRelativePath(
                assertionAttachment.Path
            );
            var attachmentFileName = Path.GetFileName(attachmentPath);
            if (string.IsNullOrWhiteSpace(attachmentFileName))
                throw new InvalidOperationException(
                    "Assertion attachment path must include a file name."
                );

            var serializer = SerializerFactory.BuildSerializer(
                assertionAttachment.SerializationType
            );
            var assertionData =
                serializer?.Serialize(assertionAttachment.Data)
                ?? (assertionAttachment.Data != null ? (byte[])assertionAttachment.Data! : []);
            attachments.Add(
                SaveDataToAllure(
                    assertionData,
                    attachmentFileName,
                    Path.Join(
                        specificAssertionAttachmentDirectory,
                        Path.GetDirectoryName(attachmentPath) ?? string.Empty
                    ),
                    attachmentPath,
                    GetAttachmentTypeBySerializationType(assertionAttachment.SerializationType)
                )
            );
        }

        return attachments;
    }

    private Attachment SaveDataToAllure(
        byte[] data,
        string fileName,
        string attachmentDirectory,
        string name,
        string type
    )
    {
        return new Attachment
        {
            name = name,
            source = SaveAttachmentIfNotAlreadySaved(data, attachmentDirectory, fileName, type),
            type = type,
        };
    }

    private List<Attachment> GetCoveragesAsAttachments(AssertionResult assertionResult)
    {
        const string coverageDir = "Coverages";
        var assertionSessionNames = assertionResult.Assertion.SessionDataList.Select(session =>
            session.Name
        );
        var contextCoverageFiles = new List<string>();
        var fullCoverageDirectory = Path.Combine(
            AllureLifecycle.Instance.ResultsDirectory,
            coverageDir
        );
        if (FileSystem.Directory.Exists(fullCoverageDirectory))
            contextCoverageFiles = FileSystem
                .Directory.EnumerateFiles(fullCoverageDirectory)
                .Select(Path.GetFileName)
                .Where(fileName => fileName != null)
                .ToList()!;
        if (Context.ExecutionId != null)
            contextCoverageFiles = contextCoverageFiles
                .Where(fileName => fileName.Contains(Context.ExecutionId))
                .ToList();
        if (Context.CaseName != null)
            contextCoverageFiles = contextCoverageFiles
                .Where(fileName => fileName.Contains(Context.CaseName))
                .ToList();

        var attachments = new List<Attachment>();
        foreach (var sessionName in assertionSessionNames)
        foreach (
            var sessionCoverageFile in contextCoverageFiles.Where(fileName =>
                fileName.Contains(sessionName)
            )
        )
            attachments.Add(
                SaveDataToAllure(
                    FileSystem.File.ReadAllBytes(
                        Path.Combine(fullCoverageDirectory, sessionCoverageFile)
                    ),
                    sessionCoverageFile,
                    coverageDir,
                    sessionCoverageFile,
                    XmlAttachmentType
                )
            );

        return attachments;
    }

    private List<Attachment> GetAttachmentsForAssertion(AssertionResult assertionResult)
    {
        var attachments = new List<Attachment>();
        if (ShouldSaveAttachments(assertionResult.Assertion))
            attachments.AddRange(SaveAssertionAttachmentsToAllure(assertionResult));
        if (ShouldSaveTemplate(assertionResult.Assertion))
            attachments.Add(SaveConfigurationTemplateToAllure(Context.RootConfiguration));
        attachments.AddRange(GetCoveragesAsAttachments(assertionResult));
        return attachments;
    }

    private void ValidateAssertionAttachments(AssertionResult assertionResult)
    {
        var assertionAttachments =
            assertionResult.Assertion.AssertionHook?.AssertionAttachments ?? [];
        var normalizedPaths = assertionAttachments
            .Select(attachment => RunnerFileSystemExtensions.NormalizeRelativePath(attachment.Path))
            .ToList();
        var duplicatePaths = normalizedPaths
            .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(paths => paths.Count() > 1)
            .Select(paths => paths.Key)
            .ToList();
        if (duplicatePaths.Count > 0)
        {
            Context.Logger.LogDebug(
                "Duplicate attachment paths found: {Paths}",
                string.Join(", ", duplicatePaths)
            );
            throw new InvalidOperationException(
                $"Found duplicate attachment paths for assertion {assertionResult.Assertion.Name}"
            );
        }

        if (normalizedPaths.Any(path => string.IsNullOrWhiteSpace(Path.GetFileName(path))))
            throw new InvalidOperationException(
                "Assertion attachment path must include a file name."
            );
    }

    private List<Label> AddTestCaseLabelsIfIsPartOfTestCase(List<Label> existingLabels)
    {
        if (Context.CaseName == null)
            return existingLabels;
        return existingLabels
            .Concat(new[] { Label.Suite(Context.CaseName), Label.Tag(Context.CaseName) })
            .ToList();
    }

    private List<Label> AddExecutionIdLabelsIfIsUnderAnExecutionId(List<Label> existingLabels)
    {
        if (Context.ExecutionId == null)
            return existingLabels;
        return existingLabels
            .Concat(
                new[] { Label.ParentSuite(Context.ExecutionId), Label.Tag(Context.ExecutionId) }
            )
            .ToList();
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
            flaky = assertionResult.Flaky.IsFlaky,
        };
        var brokenStatusDetails = new StatusDetails
        {
            message = assertionResult.BrokenAssertionException?.Message ?? string.Empty,
            trace = displayTrace
                ? assertionResult.BrokenAssertionException?.ToString() ?? string.Empty
                : TraceDisplayFalseMessage,
            flaky = assertionResult.Flaky.IsFlaky,
        };
        return assertionResult.AssertionStatus switch
        {
            AssertionStatus.Passed => normalStatusDetails,
            AssertionStatus.Failed => normalStatusDetails,
            AssertionStatus.Broken => brokenStatusDetails,
            AssertionStatus.Unknown => normalStatusDetails,
            AssertionStatus.Skipped => normalStatusDetails,
            _ => throw new ArgumentOutOfRangeException(
                nameof(assertionResult.AssertionStatus),
                assertionResult.AssertionStatus,
                null
            ),
        };
    }

    public override void WriteTestResults(AssertionResult assertionResult)
    {
        if (ShouldSaveAttachments(assertionResult.Assertion))
            ValidateAssertionAttachments(assertionResult);
        EnsureResultsDirectoryExists();

        // Build test result
        var dataSources =
            $"[{string.Join(", ", assertionResult.Assertion.DataSourceList?.Select(dataSource => dataSource.Name).ToArray() ?? [])}]";
        var sessionNames =
            $"[{string.Join(", ", assertionResult.Assertion.SessionDataList?.Select(session => session.Name).ToArray() ?? [])}]";
        var historyId = assertionResult.Assertion.Name + Context.ExecutionId + Context.CaseName;
        var testResultUuid = Guid.NewGuid().ToString("D");
        var testResult = new TestResult
        {
            uuid = testResultUuid,
            historyId = historyId,
            name = assertionResult.Assertion.Name,
            fullName = assertionResult.Assertion.Name,
            links = assertionResult.Links is not null
                ? assertionResult
                    .Links.Select(link => new Link { name = link.Key, url = link.Value })
                    .ToList()
                : Enumerable.Empty<Link>().ToList(),
            status = AssertionStatusToAllureStatusMap[assertionResult.AssertionStatus],
            description =
                $"```yaml\n{assertionResult.Assertion.AssertionConfiguration.BuildConfigurationAsYaml()}\n```"
                + (
                    assertionResult.Flaky.IsFlaky
                        ? ArrangeFlakinessReasons(assertionResult.Flaky.FlakinessReasons)
                        : string.Empty
                ),
            parameters =
            [
                new Parameter { name = "Session Names", value = sessionNames },
                new Parameter { name = "Data Sources", value = dataSources },
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
                    Label.Severity(
                        AssertionSeverityToAllureSeverityMap[
                            ResolveSeverity(assertionResult.Assertion)
                        ]
                    ),
                ])
            ),
            steps = assertionResult
                .Assertion.SessionDataList?.Select(sessionData =>
                    CreateSessionStep(sessionData, assertionResult.Assertion)
                )
                .ToList(),
            attachments = GetAttachmentsForAssertion(assertionResult),
            statusDetails = GetStatusDetailsAccordingToStatus(assertionResult),
        };

        // Save test result
        AllureLifecycle.Instance.StartTestCase(testResult);
        AllureLifecycle.Instance.StopTestCase(testResultUuid);
        AllureLifecycle.Instance.UpdateTestCase(
            testResultUuid,
            result =>
            {
                // update test duration to be total time of all sessions relevant to assertion + assertion
                result.start = EpochTestSuiteStartTime;
                result.stop = EpochTestSuiteStartTime + assertionResult.TestDurationMs;
            }
        );
        AllureLifecycle.Instance.WriteTestCase(testResultUuid);
    }

    private StepResult CreateSessionStep(
        SessionData sessionData,
        AssertionObjects.Assertion assertion
    )
    {
        var actionFailures = ActionFailureNormalizer.Normalize(sessionData.SessionFailures);
        var attachments = new List<Attachment>();
        if (ShouldSaveSessionData(assertion))
            attachments.Add(SaveSessionsDataToAllure(sessionData));

        var sessionLogAttachment = ShouldSaveLogs(assertion)
            ? SaveSessionLogToAllure(sessionData)
            : null;
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
                        $"[{string.Join(", ", sessionData.Inputs?.Select(input => input.Name).ToArray() ?? [])}]",
                },
                new Parameter
                {
                    name = nameof(sessionData.Outputs),
                    value =
                        $"[{string.Join(", ", sessionData.Outputs?.Select(output => output.Name).ToArray() ?? [])}]",
                },
            ],
            status = actionFailures.Count > 0 ? Status.failed : Status.passed,
            start = new DateTimeOffset(
                sessionData.UtcStartTime,
                new TimeSpan(0)
            ).ToUnixTimeMilliseconds(),
            stop = new DateTimeOffset(
                sessionData.UtcEndTime,
                new TimeSpan(0)
            ).ToUnixTimeMilliseconds(),
            attachments = attachments.Count == 0 ? null : attachments,
            steps =
                actionFailures.Count > 0
                    ? new List<StepResult>
                    {
                        new()
                        {
                            name = nameof(sessionData.SessionFailures),
                            status = Status.failed,
                            steps = actionFailures.Select(CreateActionFailureStep).ToList(),
                        },
                    }
                    : null,
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
                new Parameter { name = nameof(actionFailure.Name), value = actionFailure.Name },
                new Parameter
                {
                    name = nameof(actionFailure.ActionType),
                    value = actionFailure.ActionType,
                },
                new Parameter
                {
                    name = nameof(actionFailure.Reason.Message),
                    value = actionFailure.Reason.Message,
                },
                new Parameter
                {
                    name = nameof(actionFailure.Reason.Description),
                    value = actionFailure.Reason.Description,
                },
            ],
        };
    }

    private static string ArrangeFlakinessReasons(
        IEnumerable<KeyValuePair<string, List<ActionFailure>>> flakinessReasons
    )
    {
        return "\n### Flakiness Reasons"
            + string.Join(
                "\n",
                flakinessReasons.SelectMany(sessionNameAndFailurePair =>
                    ActionFailureNormalizer
                        .Normalize(sessionNameAndFailurePair.Value)
                        .Select(sessionFailure =>
                            $@"
- **Session {nameof(SessionData.Name)}:** `{sessionNameAndFailurePair.Key}`
  **{nameof(sessionFailure.Action)}:** `{sessionFailure.Action}`
  **{nameof(sessionFailure.ActionType)}:** `{sessionFailure.ActionType}`
  **{nameof(sessionFailure.Name)}:** `{sessionFailure.Name}`
  **{nameof(sessionFailure.Reason.Message)}:** `{sessionFailure.Reason.Message}`"
                        )
                )
            );
    }
}
