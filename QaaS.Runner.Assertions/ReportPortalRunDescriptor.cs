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
    DateTimeOffset startedAtLocal,
    IReadOnlyDictionary<string, string>? launchAttributes = null)
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
    public IReadOnlyDictionary<string, string> LaunchAttributes { get; } =
        new Dictionary<string, string>(launchAttributes ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the default ReportPortal launch title for this runner invocation.
    /// The title stays stable across runs so ReportPortal widgets can group by the same
    /// team/system/session identity without being fragmented by timestamps.
    /// </summary>
    public string BuildDefaultLaunchName()
    {
        return string.IsNullOrWhiteSpace(TeamName)
            ? $"QaaS Run | {SystemName} | {BuildSessionSummary()}"
            : $"QaaS Run | {TeamName} | {SystemName} | {BuildSessionSummary()}";
    }

    /// <summary>
    /// Builds the default ReportPortal launch description for this runner invocation.
    /// </summary>
    public string BuildDefaultDescription()
    {
        var launchAttributeSummary = LaunchAttributes.Count == 0
            ? "No additional launch attributes."
            : string.Join(", ",
                LaunchAttributes.OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(attribute => $"{attribute.Key}={attribute.Value}"));
        return
            $"QaaS captured this {ExecutionMode} directly from the runner pipeline: live sessions, real assertion outcomes, and the exact shape of {SystemName} at {StartedAtLocal:yyyy-MM-dd HH:mm:ss}. Sessions=[{string.Join(", ", SessionNames)}]. LaunchAttributes=[{launchAttributeSummary}]";
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
