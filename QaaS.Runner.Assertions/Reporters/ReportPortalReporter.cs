using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Runner.Infrastructure;
using ReportPortal.Client.Abstractions.Models;
using ReportPortal.Client.Abstractions.Requests;
using RpLogLevel = ReportPortal.Client.Abstractions.Models.LogLevel;
using RpStatus = ReportPortal.Client.Abstractions.Models.Status;
using AssertionStatus = QaaS.Framework.SDK.Hooks.Assertion.AssertionStatus;

namespace QaaS.Runner.Assertions.Reporters;

/// <inheritdoc />
public class ReportPortalReporter : BaseReporter
{
    private readonly IReportPortalLaunchAccessor _launchAccessor;

    public ReportPortalReporter(IReportPortalLaunchAccessor launchAccessor)
    {
        _launchAccessor = launchAccessor;
    }

    protected override void WriteReportCase(ReportCase reportCase)
    {
        if (!_launchAccessor.IsStarted)
        {
            Context.Logger.LogDebug("ReportPortal launch is not started. Skipping ReportPortal report case {ReportCaseName}.",
                reportCase.Name);
            return;
        }

        var launchUuid = _launchAccessor.LaunchUuid;
        var client = _launchAccessor.Client;
        var testItem = client.StartTestItemAsync(new StartTestItemRequest
        {
            LaunchUuid = launchUuid,
            Name = reportCase.Name,
            Description = reportCase.Description,
            StartTime = reportCase.Start,
            Type = TestItemType.Test,
            UniqueId = reportCase.UniqueId,
            TestCaseId = reportCase.UniqueId,
            CodeReference = reportCase.FullName,
            HasStats = true,
            Parameters = ToParameters(reportCase.Parameters),
            Attributes = BuildCaseAttributes(reportCase)
        }).GetAwaiter().GetResult();

        WriteReportCaseLogs(testItem.Uuid, reportCase);
        WriteAttachments(testItem.Uuid, reportCase.Attachments);
        foreach (var step in reportCase.Steps)
            WriteStep(testItem.Uuid, launchUuid, step);

        client.FinishTestItemAsync(testItem.Uuid, new FinishTestItemRequest
        {
            LaunchUuid = launchUuid,
            Description = BuildFinishDescription(reportCase.StatusDetails),
            EndTime = reportCase.Stop,
            Status = ToReportPortalStatus(reportCase.Status),
            Attributes = BuildCaseAttributes(reportCase)
        }).GetAwaiter().GetResult();
    }

    private QaaS.Framework.SDK.MetaDataConfig ResolveMetadata()
    {
        if (Context is not InternalContext internalContext)
            throw new InvalidOperationException("ReportPortal project must be resolved from runner metadata.");

        var metadata = internalContext.GetValueFromGlobalDictionary(internalContext.GetMetaDataPath()) as QaaS.Framework.SDK.MetaDataConfig ??
                       internalContext.GetMetaDataOrDefault();
        return metadata;
    }

    private List<ItemAttribute> BuildCaseAttributes(ReportCase reportCase)
    {
        var metadata = ResolveMetadata();
        var attributes = new List<ItemAttribute>
        {
            Attribute("tag", QaaSTag),
            Attribute("severity", reportCase.Severity.ToString()),
            Attribute("assertionType", reportCase.AssertionType),
            Attribute("flaky", reportCase.IsFlaky.ToString())
        };

        if (!string.IsNullOrWhiteSpace(metadata.System))
            attributes.Add(Attribute("system", metadata.System));
        if (!string.IsNullOrWhiteSpace(Context.ExecutionId))
            attributes.Add(Attribute("executionId", Context.ExecutionId));
        if (!string.IsNullOrWhiteSpace(Context.CaseName))
            attributes.Add(Attribute("caseName", Context.CaseName));

        return attributes;
    }

    private void WriteReportCaseLogs(string itemUuid, ReportCase reportCase)
    {
        WriteLog(itemUuid, "Description", reportCase.Description, RpLogLevel.Info);
        WriteLog(itemUuid, "Status Message", reportCase.StatusDetails.Message, RpLogLevel.Info);
        WriteLog(itemUuid, "Trace", reportCase.StatusDetails.Trace,
            reportCase.Status is AssertionStatus.Failed or AssertionStatus.Broken ? RpLogLevel.Error : RpLogLevel.Info);

        foreach (var link in reportCase.Links)
            WriteLog(itemUuid, $"Link: {link.Name}", link.Url, RpLogLevel.Info);
    }

    private void WriteStep(string parentItemUuid, string launchUuid, ReportStep step)
    {
        var client = _launchAccessor.Client;
        var stepItem = client.StartChildTestItemAsync(parentItemUuid, new StartTestItemRequest
        {
            LaunchUuid = launchUuid,
            Name = step.Name,
            Description = step.Description,
            StartTime = step.Start ?? TestSuiteStartTimeUtc,
            Type = TestItemType.Step,
            HasStats = false,
            Parameters = ToParameters(step.Parameters),
            Attributes =
            [
                Attribute("tag", QaaSTag)
            ]
        }).GetAwaiter().GetResult();

        if (!string.IsNullOrWhiteSpace(step.Description))
            WriteLog(stepItem.Uuid, "Description", step.Description, RpLogLevel.Info);
        WriteAttachments(stepItem.Uuid, step.Attachments);
        foreach (var childStep in step.Steps)
            WriteStep(stepItem.Uuid, launchUuid, childStep);

        client.FinishTestItemAsync(stepItem.Uuid, new FinishTestItemRequest
        {
            LaunchUuid = launchUuid,
            Description = step.Description,
            EndTime = step.Stop ?? step.Start ?? TestSuiteStartTimeUtc,
            Status = ToReportPortalStatus(step.Status)
        }).GetAwaiter().GetResult();
    }

    private void WriteAttachments(string itemUuid, IEnumerable<ReportAttachment> attachments)
    {
        foreach (var attachment in attachments)
        {
            if (attachment.Content == null)
                continue;

            _launchAccessor.Client.CreateLogItemAsync(new CreateLogItemRequest
            {
                TestItemUuid = itemUuid,
                Time = DateTime.UtcNow,
                Level = RpLogLevel.Info,
                Text = attachment.Name,
                Attach = new LogItemAttach(attachment.Type, attachment.Content)
                {
                    Name = attachment.FileName
                }
            }).GetAwaiter().GetResult();
        }
    }

    private void WriteLog(string itemUuid, string title, string? text, RpLogLevel level)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        _launchAccessor.Client.CreateLogItemAsync(new CreateLogItemRequest
        {
            TestItemUuid = itemUuid,
            Time = DateTime.UtcNow,
            Level = level,
            Text = $"{title}: {text}"
        }).GetAwaiter().GetResult();
    }

    private static IList<KeyValuePair<string, string>> ToParameters(IEnumerable<ReportParameter> parameters)
    {
        return parameters
            .Select(parameter => new KeyValuePair<string, string>(parameter.Name, parameter.Value))
            .ToList();
    }

    private static string BuildFinishDescription(ReportStatusDetails details)
    {
        return string.Join(Environment.NewLine, new[]
        {
            details.Message,
            details.Trace
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static ItemAttribute Attribute(string key, string value)
    {
        return new ItemAttribute
        {
            Key = key,
            Value = value
        };
    }

    private static RpStatus ToReportPortalStatus(AssertionStatus status)
    {
        return status switch
        {
            AssertionStatus.Passed => RpStatus.Passed,
            AssertionStatus.Failed => RpStatus.Failed,
            AssertionStatus.Broken => RpStatus.Failed,
            AssertionStatus.Skipped => RpStatus.Skipped,
            AssertionStatus.Unknown => RpStatus.Info,
            _ => RpStatus.Info
        };
    }
}
