using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QaaS.Runner.E2ETests.Assertions.ReportPortal;

/// <summary>
/// Configures the rich ReportPortal E2E assertion used to exercise message, trace, attachment, and failure reporting.
/// </summary>
public sealed class ReportPortalRichAssertionConfig
{
    [Description("Controls whether the assertion passes, fails, or breaks with an exception.")]
    public ReportPortalAssertionOutcome Outcome { get; set; } = ReportPortalAssertionOutcome.Passed;

    [Description("The human-readable assertion message written into Allure and ReportPortal.")]
    public string Message { get; set; } = "ReportPortal E2E assertion message";

    [Description("Prefix used to build the multi-line assertion trace.")]
    public string TracePrefix { get; set; } = "ReportPortal E2E assertion trace";

    [Range(0, 10)]
    [Description("How many attachment batches to emit for this assertion.")]
    public int AttachmentCount { get; set; } = 2;

    [Description("Attachment formats emitted by this assertion.")]
    public List<ReportPortalAttachmentKind> AttachmentKinds { get; set; } =
    [
        ReportPortalAttachmentKind.Json,
        ReportPortalAttachmentKind.Yaml,
        ReportPortalAttachmentKind.Binary
    ];

    [Description("Additional free-form text sections appended to the assertion trace.")]
    public List<string> AdditionalTraceSections { get; set; } = [];

    [Range(1, 10)]
    [Description("How many times to repeat the trace sections to create larger diagnostic traces.")]
    public int TraceSectionRepeats { get; set; } = 1;

    [Description("Whether to include a session-failure rollup in the assertion message.")]
    public bool IncludeFailureRollupInMessage { get; set; } = true;
}

/// <summary>
/// Supported E2E assertion outcomes.
/// </summary>
public enum ReportPortalAssertionOutcome
{
    Passed,
    Failed,
    Broken
}

/// <summary>
/// Supported attachment formats emitted by the rich E2E assertion.
/// </summary>
public enum ReportPortalAttachmentKind
{
    Json,
    Yaml,
    Binary
}
