namespace QaaS.Runner.Assertions;

/// <summary>
/// Captures the runner-scoped identity for a single ReportPortal launch.
/// The descriptor is built once before executions start so all assertions share one consistent launch title,
/// description, and session/system context.
/// </summary>
public sealed class ReportPortalRunDescriptor(
    string? teamName,
    string? systemName,
    IReadOnlyList<string> sessionNames,
    string executionMode,
    DateTimeOffset startedAtLocal)
{
    public string? TeamName { get; } = string.IsNullOrWhiteSpace(teamName) ? null : teamName.Trim();
    public string SystemName { get; } = string.IsNullOrWhiteSpace(systemName) ? "Unknown System" : systemName.Trim();
    public IReadOnlyList<string> SessionNames { get; } = sessionNames
        .Where(sessionName => !string.IsNullOrWhiteSpace(sessionName))
        .Select(sessionName => sessionName.Trim())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(sessionName => sessionName, StringComparer.Ordinal)
        .ToArray();
    public string ExecutionMode { get; } = string.IsNullOrWhiteSpace(executionMode) ? "run" : executionMode.Trim();
    public DateTimeOffset StartedAtLocal { get; } = startedAtLocal;

    /// <summary>
    /// Builds the default ReportPortal launch title for this runner invocation.
    /// </summary>
    public string BuildDefaultLaunchName()
    {
        return $"QaaS Run | {SystemName} | {BuildSessionSummary()} | {StartedAtLocal:yyyy-MM-dd HH:mm:ss}";
    }

    /// <summary>
    /// Builds the default ReportPortal launch description for this runner invocation.
    /// </summary>
    public string BuildDefaultDescription()
    {
        return $"QaaS captured this run directly from the runner pipeline: live sessions, real assertion outcomes, and the exact shape of {SystemName} at {StartedAtLocal:yyyy-MM-dd HH:mm:ss}.";
    }

    private string BuildSessionSummary()
    {
        if (SessionNames.Count == 0)
            return "No Sessions";

        if (SessionNames.Count <= 2)
            return string.Join(", ", SessionNames);

        return $"{SessionNames[0]}, {SessionNames[1]}(+{SessionNames.Count - 2})";
    }
}
