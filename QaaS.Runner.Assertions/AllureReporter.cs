using System.Collections.Concurrent;
using System.Reflection;
using Allure.Commons;
using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Infrastructure;
using RunnerFileSystemExtensions = QaaS.Runner.Infrastructure.FileSystemExtensions;
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
            { AssertionStatus.Unknown, Status.none },
            { AssertionStatus.Skipped, Status.skipped }
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

    protected override string AttachmentRootDirectory => AllureLifecycle.Instance.ResultsDirectory;

    protected override void WriteReportCase(ReportCase reportCase)
    {
        EnsureResultsDirectoryExists();

        var testResult = new TestResult
        {
            uuid = reportCase.UniqueId,
            rerunOf = reportCase.UniqueId,
            historyId = reportCase.UniqueId,
            name = reportCase.Name,
            fullName = reportCase.FullName,
            links = reportCase.Links.Select(ToAllureLink).ToList(),
            status = ToAllureStatus(reportCase.Status),
            description = reportCase.Description,
            parameters = reportCase.Parameters.Select(ToAllureParameter).ToList(),
            labels = BuildLabels(reportCase),
            steps = reportCase.Steps.Select(ToAllureStep).ToList(),
            attachments = reportCase.Attachments.Select(SaveReportAttachment).ToList(),
            statusDetails = ToAllureStatusDetails(reportCase.StatusDetails)
        };

        AllureLifecycle.Instance.StartTestCase(testResult);
        AllureLifecycle.Instance.StopTestCase(reportCase.UniqueId);
        AllureLifecycle.Instance.UpdateTestCase(reportCase.UniqueId, result =>
        {
            result.start = reportCase.Start;
            result.stop = reportCase.Stop;
        });
        AllureLifecycle.Instance.WriteTestCase(reportCase.UniqueId);
    }

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

    private void EnsureResultsDirectoryExists()
    {
        var resultsDirectory = Path.GetFullPath(AllureLifecycle.Instance.ResultsDirectory);
        if (!FileSystem.Directory.Exists(resultsDirectory))
            FileSystem.Directory.CreateDirectory(resultsDirectory);
    }

    private Attachment SaveReportAttachment(ReportAttachment reportAttachment)
    {
        if (reportAttachment.Content != null)
            SaveAttachmentIfNotAlreadySaved(reportAttachment.Content, reportAttachment.Directory,
                reportAttachment.FileName);

        return new Attachment
        {
            name = reportAttachment.Name,
            source = reportAttachment.Source,
            type = reportAttachment.Type
        };
    }

    private List<Label> BuildLabels(ReportCase reportCase)
    {
        var labels = AddExecutionIdLabelsIfIsUnderAnExecutionId(
            AddTestCaseLabelsIfIsPartOfTestCase([
                Label.TestClass(Context.ExecutionId),
                Label.TestType(reportCase.AssertionType),
                Label.Epic(GetParameterValue(reportCase, "Session Names")),
                Label.Feature(reportCase.AssertionType),
                Label.Package(Assembly.GetEntryAssembly()?.GetName().Name ?? QaaSTag),
                Label.Tag(QaaSTag),
                Label.Tag(reportCase.AssertionType),
                Label.Host(),
                Label.Severity(AssertionSeverityToAllureSeverityMap[reportCase.Severity])
            ]));
        return labels;
    }

    private static string GetParameterValue(ReportCase reportCase, string parameterName)
    {
        return reportCase.Parameters.FirstOrDefault(parameter => parameter.Name == parameterName)?.Value ?? string.Empty;
    }

    private static Link ToAllureLink(ReportLink reportLink)
    {
        return new Link
        {
            name = reportLink.Name,
            url = reportLink.Url
        };
    }

    private static Parameter ToAllureParameter(ReportParameter reportParameter)
    {
        return new Parameter
        {
            name = reportParameter.Name,
            value = reportParameter.Value
        };
    }

    private StepResult ToAllureStep(ReportStep reportStep)
    {
        return new StepResult
        {
            name = reportStep.Name,
            description = reportStep.Description,
            status = ToAllureStatus(reportStep.Status),
            start = reportStep.Start ?? 0,
            stop = reportStep.Stop ?? 0,
            parameters = reportStep.Parameters.Select(ToAllureParameter).ToList(),
            attachments = reportStep.Attachments.Count == 0
                ? null
                : reportStep.Attachments.Select(SaveReportAttachment).ToList(),
            steps = reportStep.Steps.Count == 0
                ? null
                : reportStep.Steps.Select(ToAllureStep).ToList()
        };
    }

    private static StatusDetails ToAllureStatusDetails(ReportStatusDetails reportStatusDetails)
    {
        return new StatusDetails
        {
            message = reportStatusDetails.Message,
            trace = reportStatusDetails.Trace,
            flaky = reportStatusDetails.Flaky
        };
    }

    private static Status ToAllureStatus(AssertionStatus assertionStatus) =>
        AssertionStatusToAllureStatusMap[assertionStatus];

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

    private new string GetAttachmentDirectory(string baseAttachmentDirectoryInsideAllureDirectory,
        string? extraSubDirectoryName = null)
    {
        return base.GetAttachmentDirectory(baseAttachmentDirectoryInsideAllureDirectory, extraSubDirectoryName);
    }

    private Attachment SaveSessionsDataToAllure(SessionData sessionData)
    {
        Context.Logger.LogDebug("Saving session data for {SessionName} as an Allure attachment", sessionData.Name);
        return SaveReportAttachment(BuildSessionDataAttachment(sessionData));
    }

    private Attachment SaveConfigurationTemplateToAllure(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        Context.Logger.LogDebug("Saving the execution configuration template as an Allure attachment");
        return SaveReportAttachment(BuildConfigurationTemplateAttachment(configuration));
    }

    private Attachment? SaveSessionLogToAllure(SessionData sessionData)
    {
        var reportAttachment = BuildSessionLogAttachment(sessionData);
        return reportAttachment == null ? null : SaveReportAttachment(reportAttachment);
    }

    private List<Attachment> SaveAssertionAttachmentsToAllure(AssertionResult assertionResult)
    {
        return BuildAssertionAttachments(assertionResult).Select(SaveReportAttachment).ToList();
    }

    private Attachment SaveDataToAllure(byte[] data, string fileName, string attachmentDirectory, string name,
        string type)
    {
        return SaveReportAttachment(BuildAttachment(data, fileName, attachmentDirectory, name, type));
    }

    private List<Attachment> GetCoveragesAsAttachments(AssertionResult assertionResult)
    {
        return BuildCoverageAttachments(assertionResult).Select(SaveReportAttachment).ToList();
    }

    private List<Attachment> GetAttachmentsForAssertion(AssertionResult assertionResult)
    {
        return BuildAttachmentsForAssertion(assertionResult).Select(SaveReportAttachment).ToList();
    }

    private StatusDetails GetStatusDetailsAccordingToStatus(AssertionResult assertionResult)
    {
        return ToAllureStatusDetails(BuildStatusDetails(assertionResult));
    }

    private StepResult CreateSessionStep(SessionData sessionData, AssertionObjects.Assertion assertion)
    {
        return ToAllureStep(BuildSessionStep(sessionData, assertion));
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
}
