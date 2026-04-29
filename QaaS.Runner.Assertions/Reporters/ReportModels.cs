using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Framework.SDK.Hooks.Assertion;

namespace QaaS.Runner.Assertions.Reporters;

public sealed class ReportCase
{
    public string UniqueId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public AssertionStatus Status { get; init; }
    public AssertionSeverity Severity { get; init; }
    public string AssertionType { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public long Start { get; init; }
    public long Stop { get; init; }
    public bool IsFlaky { get; init; }
    public List<ReportLink> Links { get; init; } = [];
    public List<ReportParameter> Parameters { get; init; } = [];
    public List<ReportStep> Steps { get; init; } = [];
    public List<ReportAttachment> Attachments { get; init; } = [];
    public ReportStatusDetails StatusDetails { get; init; } = new();
}

public sealed class ReportStep
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public AssertionStatus Status { get; init; }
    public long? Start { get; init; }
    public long? Stop { get; init; }
    public List<ReportParameter> Parameters { get; init; } = [];
    public List<ReportAttachment> Attachments { get; init; } = [];
    public List<ReportStep> Steps { get; init; } = [];
}

public sealed class ReportAttachment
{
    public string Name { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Directory { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public byte[]? Content { get; init; }
}

public sealed class ReportParameter
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public sealed class ReportLink
{
    public string Name { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}

public sealed class ReportStatusDetails
{
    public string Message { get; init; } = string.Empty;
    public string Trace { get; init; } = string.Empty;
    public bool Flaky { get; init; }
}
