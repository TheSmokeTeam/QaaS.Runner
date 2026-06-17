namespace QaaS.Runner.Assertions.Reporters.ReportPortal;

/// <summary>
/// Captures the runner-scoped identity for a single ReportPortal launch.
/// The descriptor is built once before executions start so all assertions share one consistent launch title,
/// description, and session/system context.
/// </summary>
public sealed class ReportPortalLaunchDescriptor(
    string? teamName,
    string? systemName,
    IReadOnlyList<string> sessionNames,
    string executionMode,
    DateTimeOffset startedAtLocal,
    IReadOnlyDictionary<string, string>? launchAttributes = null)
{
    private const string UnknownSystem = "Unknown System";

    public string? TeamName { get; } = Clean(teamName);
    public string SystemName { get; } = Clean(systemName) ?? UnknownSystem;
    public IReadOnlyList<string> SessionNames { get; } = sessionNames
        .Select(Clean)
        .OfType<string>()
        .Distinct(StringComparer.Ordinal)
        .OrderBy(sessionName => sessionName, StringComparer.Ordinal)
        .ToArray();
    public string ExecutionMode { get; } = Clean(executionMode) ?? "run";
    public DateTimeOffset StartedAtLocal { get; } = startedAtLocal;
    public IReadOnlyDictionary<string, string> LaunchAttributes { get; } =
        CleanAttributes(launchAttributes);

    /// <summary>
    /// Builds the default ReportPortal launch title for this runner invocation.
    /// The title stays stable across runs so ReportPortal widgets can group by the same
    /// team/system/session identity without being fragmented by timestamps.
    /// </summary>
    public string BuildDefaultLaunchName()
    {
        return TeamName is null
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

    private static IReadOnlyDictionary<string, string> CleanAttributes(
        IReadOnlyDictionary<string, string>? attributes)
    {
        var cleanAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in attributes ?? new Dictionary<string, string>())
        {
            if (Clean(key) is { } cleanKey)
                cleanAttributes[cleanKey] = Clean(value) ?? string.Empty;
        }

        return cleanAttributes;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
