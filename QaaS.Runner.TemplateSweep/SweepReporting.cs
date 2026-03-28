internal static class SweepReporting
{
    public static string BuildMarkdown(IReadOnlyList<CaseResult> results, HookInventory hooks, int contextCount)
    {
        var failures = results.Where(result => !result.Success)
            .OrderBy(result => result.Domain, StringComparer.Ordinal)
            .ThenBy(result => result.Context, StringComparer.Ordinal)
            .ThenBy(result => result.FieldPath, StringComparer.Ordinal)
            .ThenBy(result => result.Mode)
            .ToList();

        var successes = results.Where(result => result.Success)
            .OrderBy(result => result.Domain, StringComparer.Ordinal)
            .ThenBy(result => result.Context, StringComparer.Ordinal)
            .ThenBy(result => result.FieldPath, StringComparer.Ordinal)
            .ThenBy(result => result.Mode)
            .ToList();

        var lines = new List<string>
        {
            "# Configuration Sweep Report",
            string.Empty,
            $"Generated: {DateTimeOffset.Now:O}",
            $"Contexts: {contextCount}",
            $"Cases: {results.Count}",
            $"Failures: {failures.Count}",
            $"Successes: {successes.Count}",
            string.Empty,
            "## Installed Packages",
            "- `QaaS.Runner` `4.2.0`",
            "- `QaaS.Mocker` `2.1.0`",
            "- `QaaS.Common.Generators` `3.2.0`",
            "- `QaaS.Common.Probes` `1.2.0`",
            "- `QaaS.Common.Assertions` `3.2.0`",
            "- `QaaS.Common.Processors` `1.2.0`",
            string.Empty,
            "## Hook Inventory",
            $"- Generators: {string.Join(", ", hooks.Generators.Hooks.Select(hook => hook.DisplayName))}",
            $"- Probes: {string.Join(", ", hooks.Probes.Hooks.Select(hook => hook.DisplayName))}",
            $"- Assertions: {string.Join(", ", hooks.Assertions.Hooks.Select(hook => hook.DisplayName))}",
            $"- Processors: {string.Join(", ", hooks.Processors.Hooks.Select(hook => hook.DisplayName))}",
            string.Empty,
            "## Failures"
        };

        if (failures.Count == 0)
        {
            lines.Add("- None");
        }
        else
        {
            foreach (var failure in failures)
            {
                lines.Add(
                    $"- `{failure.Context}` `{failure.FieldPath}` `{failure.Mode}` exit={failure.ExitCode} config=`{failure.ConfigPath}`");
                lines.Add($"  {failure.Summary.Replace(Environment.NewLine, " ")}");
            }
        }

        lines.Add(string.Empty);
        lines.Add("## Successes");

        if (successes.Count == 0)
        {
            lines.Add("- None");
        }
        else
        {
            foreach (var success in successes)
            {
                lines.Add(
                    $"- `{success.Context}` `{success.FieldPath}` `{success.Mode}` exit={success.ExitCode} config=`{success.ConfigPath}`");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}
