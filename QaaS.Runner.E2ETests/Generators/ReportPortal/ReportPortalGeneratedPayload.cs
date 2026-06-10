namespace QaaS.Runner.E2ETests.Generators.ReportPortal;

/// <summary>
/// Sample payload emitted by the ReportPortal E2E generator so assertions can attach meaningful structured data.
/// </summary>
public sealed record ReportPortalGeneratedPayload
{
    public int Index { get; init; }
    public string PayloadId { get; init; } = string.Empty;
    public string Component { get; init; } = string.Empty;
    public string Area { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;
    public string EnvironmentName { get; init; } = string.Empty;
    public string Capability { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string Scenario { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, double> Metrics { get; init; } = new Dictionary<string, double>();
    public IReadOnlyList<ReportPortalEvidenceEntry> Evidence { get; init; } = Array.Empty<ReportPortalEvidenceEntry>();
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Deterministic nested payload data used to exercise attachment serialization in the ReportPortal E2E matrix.
/// </summary>
public sealed record ReportPortalEvidenceEntry
{
    public string Title { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Hint { get; init; } = string.Empty;
}
