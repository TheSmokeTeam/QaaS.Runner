using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;

namespace QaaS.Runner.E2ETests.Assertions.ReportPortal;

/// <summary>
/// Rich assertion used by the ReportPortal E2E matrix. It emits deterministic messages, traces, and attachments so the
/// ReportPortal reporter can be validated without affecting Allure behavior.
/// </summary>
public sealed class ReportPortalRichAssertion : BaseAssertion<ReportPortalRichAssertionConfig>
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true
    };

    public override bool Assert(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        var sessionSummaries = sessionDataList.Select(sessionData => new
            {
                sessionData.Name,
                DurationMs = (long)Math.Max(0, (sessionData.UtcEndTime - sessionData.UtcStartTime).TotalMilliseconds),
                Failures = sessionData.SessionFailures.Count,
                FailureNames = sessionData.SessionFailures.Select(failure => failure.Name).ToArray(),
                Inputs = sessionData.Inputs?.Count ?? 0,
                Outputs = sessionData.Outputs?.Count ?? 0
            })
            .Cast<object>()
            .ToList();
        var dataSourceSummaries = dataSourceList.Select(dataSource => new
            {
                dataSource.Name,
                dataSource.Lazy,
                SampleBodies = dataSource.Retrieve()
                    .Take(3)
                    .Select(data => JsonSerializer.Serialize(data.Body))
                    .ToList()
            })
            .Cast<object>()
            .ToList();
        var failureCount = sessionDataList.Sum(sessionData => sessionData.SessionFailures.Count);
        var attachmentKinds = Configuration.AttachmentKinds.Distinct().ToList();

        AssertionMessage = BuildMessage(sessionSummaries.Count, dataSourceSummaries.Count, failureCount,
            attachmentKinds.Count * Configuration.AttachmentCount);
        AssertionTrace = BuildTrace(sessionSummaries, dataSourceSummaries, attachmentKinds);
        AddAttachments(sessionSummaries, dataSourceSummaries, attachmentKinds);

        return Configuration.Outcome switch
        {
            ReportPortalAssertionOutcome.Passed => true,
            ReportPortalAssertionOutcome.Failed => false,
            ReportPortalAssertionOutcome.Broken =>
                throw new InvalidOperationException($"{Configuration.Message}{Environment.NewLine}{AssertionTrace}"),
            _ => true
        };
    }

    private string BuildMessage(int sessionCount, int dataSourceCount, int failureCount, int attachmentCount)
    {
        var message = new StringBuilder()
            .Append(Configuration.Message)
            .Append(" | Sessions=")
            .Append(sessionCount)
            .Append(" | DataSources=")
            .Append(dataSourceCount)
            .Append(" | Attachments=")
            .Append(attachmentCount);

        if (Configuration.IncludeFailureRollupInMessage)
        {
            message.Append(" | SessionFailures=")
                .Append(failureCount);
        }

        return message.ToString();
    }

    private string BuildTrace(IEnumerable<object> sessionSummaries, IEnumerable<object> dataSourceSummaries,
        IReadOnlyCollection<ReportPortalAttachmentKind> attachmentKinds)
    {
        var traceBuilder = new StringBuilder()
            .AppendLine(Configuration.TracePrefix)
            .AppendLine()
            .AppendLine("Session summary:")
            .AppendLine(JsonSerializer.Serialize(sessionSummaries, PrettyJsonOptions))
            .AppendLine()
            .AppendLine("Data source summary:")
            .AppendLine(JsonSerializer.Serialize(dataSourceSummaries, PrettyJsonOptions))
            .AppendLine()
            .AppendLine("Attachment plan:")
            .AppendLine(JsonSerializer.Serialize(new
            {
                Configuration.AttachmentCount,
                AttachmentKinds = attachmentKinds.Select(kind => kind.ToString()).ToArray()
            }, PrettyJsonOptions));

        foreach (var _ in Enumerable.Range(0, Configuration.TraceSectionRepeats))
        {
            foreach (var extraTraceSection in Configuration.AdditionalTraceSections.Where(section =>
                         !string.IsNullOrWhiteSpace(section)))
            {
                traceBuilder.AppendLine()
                    .AppendLine(extraTraceSection.Trim());
            }
        }

        return traceBuilder.ToString().Trim();
    }

    private void AddAttachments(IReadOnlyList<object> sessionSummaries, IReadOnlyList<object> dataSourceSummaries,
        IReadOnlyList<ReportPortalAttachmentKind> attachmentKinds)
    {
        for (var attachmentIndex = 0; attachmentIndex < Configuration.AttachmentCount; attachmentIndex++)
        {
            foreach (var attachmentKind in attachmentKinds)
            {
                var relativePath = BuildAttachmentPath(attachmentKind, attachmentIndex);
                switch (attachmentKind)
                {
                    case ReportPortalAttachmentKind.Json:
                        AssertionAttachments.Add(new AssertionAttachment
                        {
                            Path = relativePath,
                            Data = new
                            {
                                Configuration.Message,
                                Configuration.TracePrefix,
                                Batch = attachmentIndex + 1,
                                AdditionalTraceSections = Configuration.AdditionalTraceSections,
                                Sessions = sessionSummaries,
                                DataSources = dataSourceSummaries
                            },
                            SerializationType = SerializationType.Json
                        });
                        break;
                    case ReportPortalAttachmentKind.Yaml:
                        AssertionAttachments.Add(new AssertionAttachment
                        {
                            Path = relativePath,
                            Data = new
                            {
                                outcome = Configuration.Outcome.ToString(),
                                message = Configuration.Message,
                                trace = AssertionTrace,
                                attachmentKinds = attachmentKinds.Select(kind => kind.ToString()).ToArray(),
                                sessions = sessionSummaries,
                                dataSources = dataSourceSummaries
                            },
                            SerializationType = SerializationType.Yaml
                        });
                        break;
                    case ReportPortalAttachmentKind.Binary:
                        AssertionAttachments.Add(new AssertionAttachment
                        {
                            Path = relativePath,
                            Data = Encoding.UTF8.GetBytes(
                                $"{Configuration.Message}|batch={attachmentIndex + 1}|outcome={Configuration.Outcome}"),
                            SerializationType = SerializationType.Binary
                        });
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(attachmentKind), attachmentKind, null);
                }
            }
        }
    }

    private static string BuildAttachmentPath(ReportPortalAttachmentKind attachmentKind, int attachmentIndex)
    {
        var extension = attachmentKind switch
        {
            ReportPortalAttachmentKind.Json => "json",
            ReportPortalAttachmentKind.Yaml => "yaml",
            ReportPortalAttachmentKind.Binary => "bin",
            _ => throw new ArgumentOutOfRangeException(nameof(attachmentKind), attachmentKind, null)
        };

        return Path.Combine("reportportal", "rich-assertion",
            $"{attachmentKind.ToString().ToLowerInvariant()}-{attachmentIndex + 1}.{extension}");
    }
}
