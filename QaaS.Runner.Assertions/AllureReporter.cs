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
using AssertionResult = QaaS.Runner.Assertions.AssertionObjects.AssertionResult;
using AssertionSeverity = QaaS.Runner.Assertions.AssertionObjects.AssertionSeverity;


namespace QaaS.Runner.Assertions;

/// <inheritdoc />
public class AllureReporter : BaseReporter
{
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

    private readonly ConcurrentBag<string> _alreadySavedAttachments = new();

    /// <summary>
    ///     Saves an attachment as a file in the allure results directory, if it was already saved doesn't save it again
    /// </summary>
    protected virtual void SaveAttachmentIfNotAlreadySaved(byte[] attachmentContent,
        string attachmentDirectory, string attachmentFileName)
    {
        var attachmentUuid = string.Concat(attachmentDirectory, attachmentFileName);
        if (_alreadySavedAttachments.Contains(attachmentUuid)) return;

        var attachmentDirectoryPath = Path.Join(AllureLifecycle.Instance.ResultsDirectory, attachmentDirectory);
        if (!FileSystem.Directory.Exists(attachmentDirectoryPath))
            FileSystem.Directory.CreateDirectory(attachmentDirectoryPath);

        var attachmentFullPath = Path.Join(attachmentDirectoryPath, attachmentFileName);
        FileSystem.File.WriteAllBytes(attachmentFullPath, attachmentContent);
        _alreadySavedAttachments.Add(attachmentUuid);
        Context.Logger.LogDebug("Saved attachment to {AttachmentFullPath}", attachmentFullPath);
    }

    private string GetAttachmentDirectory(string baseAttachmentDirectoryInsideAllureDirectory,
        string? extraSubDirectoryName = null)
    {
        var currentAttachmentDirectory = Path.Join(baseAttachmentDirectoryInsideAllureDirectory,
            $"{EpochTestSuiteStartTime}");
        var executionAttachmentsDirectory = Context.ExecutionId == null
            ? currentAttachmentDirectory
            : Path.Join(currentAttachmentDirectory, Context.ExecutionId);
        var caseAttachmentDirectory = Context.CaseName == null
            ? executionAttachmentsDirectory
            : Path.Join(executionAttachmentsDirectory,
                FileSystemExtensions.MakeValidDirectoryName(Context.CaseName));
        return extraSubDirectoryName == null
            ? caseAttachmentDirectory
            : Path.Join(caseAttachmentDirectory, extraSubDirectoryName);
    }

    private Attachment SaveSessionsDataToAllure(SessionData sessionData)
    {
        const string sessionAttachmentsDirectory = "SessionsData";
        var sessionDataAttachmentDirectory = GetAttachmentDirectory(sessionAttachmentsDirectory);
        var attachmentFile = $"{sessionData.Name}.json";
        Context.Logger.LogDebug("Storing {SessionName} session data items relevant to the assertion as attachments"
            , sessionData.Name);
        return SaveDataToAllure(data: SessionDataSerialization.SerializeSessionData(sessionData,
                new JsonSerializerOptions
                {
                    WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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
        Context.Logger.LogDebug("Storing qaas-template result - relevant to the assertion - as attachments");
        return SaveDataToAllure(
            data: Encoding.UTF8.GetBytes(
                configuration.BuildConfigurationAsYaml(Infrastructure.Constants.ConfigurationSectionNames)),
            fileName: attachmentFile,
            attachmentDirectory: templateAttachmentsDirectoryFullPath,
            name: attachmentFile,
            type: YamlAttachmentType);
    }

    private List<Attachment> SaveAssertionAttachmentsToAllure(AssertionResult assertionResult)
    {
        const string assertionsAttachmentsDirectory = "AssertionsAttachments";
        var specificAssertionAttachmentDirectory = GetAttachmentDirectory(assertionsAttachmentsDirectory,
            assertionResult.Assertion.Name);

        var attachments = new List<Attachment>();
        Context.Logger.LogDebug("Storing {AssertionName} assertion's attachments",
            assertionResult.Assertion.Name);

        // validating unique paths
        var assertionAttachmentsPaths =
            assertionResult.Assertion.AssertionHook?.AssertionAttachments.Select(attachment => attachment.Path);
        var duplicatePaths = assertionAttachmentsPaths?.GroupBy(path => path).Where(paths => paths.Count() > 1)
            .Select(item => item.Key).ToList();
        if (duplicatePaths != null && duplicatePaths.Count != 0)
        {
            Context.Logger.LogDebug("Duplicate attachment paths found: {Paths}", string.Join(", ", duplicatePaths));
            throw new InvalidOperationException(
                $"Found duplicate attachment paths for assertion {assertionResult.Assertion.Name}");
        }

        foreach (var assertionAttachment in assertionResult.Assertion.AssertionHook?.AssertionAttachments ?? [])
        {
            var attachmentPath = assertionAttachment.Path;
            var attachmentFileName = Path.GetFileName(attachmentPath);
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

    /// <summary>
    ///     Saves serialized data to file inside allure-results directory and stores it as attachment.
    /// </summary>
    /// <param name="data">The serialized data to store</param>
    /// <param name="fileName">The file's name</param>
    /// <param name="attachmentDirectory">The directory to store the attachment inside allure-result directory</param>
    /// <param name="name">Visual name for the attachment</param>
    /// <param name="type">Serialization type</param>
    /// <returns></returns>
    private Attachment SaveDataToAllure(byte[] data, string fileName, string attachmentDirectory, string name,
        string type)
    {
        SaveAttachmentIfNotAlreadySaved(data, attachmentDirectory, fileName);
        return new Attachment
        {
            name = name,
            source = Path.Join(attachmentDirectory, fileName),
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
            contextCoverageFiles = Directory.EnumerateFiles(fullCoverageDirectory).Select(Path.GetFileName)
                .Where(fileName => fileName != null).ToList()!;
        if (Context.ExecutionId != null)
            contextCoverageFiles = contextCoverageFiles.Where(fileName => fileName.Contains(Context.ExecutionId))
                .ToList();
        if (Context.CaseName != null)
            contextCoverageFiles =
                contextCoverageFiles.Where(fileName => fileName.Contains(Context.CaseName)).ToList();
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

        if (SaveAttachments)
            attachments = attachments.Concat(SaveAssertionAttachmentsToAllure(assertionResult)).ToList();
        if (SaveTemplate)
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
        var normalStatusDetails = new StatusDetails
        {
            message = assertionResult.Assertion.AssertionHook?.AssertionMessage ?? string.Empty,
            trace = DisplayTrace
                ? assertionResult.Assertion.AssertionHook?.AssertionTrace ?? string.Empty
                : TraceDisplayFalseMessage,
            flaky = assertionResult.Flaky.IsFlaky
        };
        var brokenStatusDetails = new StatusDetails
        {
            message = assertionResult.BrokenAssertionException?.Message ?? string.Empty,
            trace = DisplayTrace
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

    /// <summary>
    /// Writes assertion test results to the Allure report
    /// </summary>
    /// <param name="assertionResult">The assertion result to write</param>
    public override void WriteTestResults(AssertionResult assertionResult)
    {
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
                        [Severity])
                ])),
            steps = assertionResult.Assertion.SessionDataList?.Select(CreateSessionStep).ToList(),
            statusDetails = GetStatusDetailsAccordingToStatus(assertionResult),
            attachments = GetAttachmentsForAssertion(assertionResult)
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

    private StepResult CreateSessionStep(SessionData sessionData)
    {
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
            attachments = SaveSessionData ? [SaveSessionsDataToAllure(sessionData)] : null,
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
}