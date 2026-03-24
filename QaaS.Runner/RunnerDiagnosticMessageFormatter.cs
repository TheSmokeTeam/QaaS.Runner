using System.Text;

namespace QaaS.Runner;

internal static class RunnerDiagnosticMessageFormatter
{
    public static string Format(
        string headline,
        IEnumerable<string?>? contextLines = null,
        string? detailHeading = null,
        IEnumerable<string?>? detailLines = null,
        IEnumerable<string?>? guidanceLines = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine(headline);

        AppendBulletedSection(builder, "Context", contextLines);
        AppendNumberedSection(builder, detailHeading, detailLines);
        AppendBulletedSection(builder, "How to fix", guidanceLines);

        return builder.ToString().TrimEnd();
    }

    public static string SummarizeValues(IEnumerable<string?>? values)
    {
        var materializedValues = values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];

        return materializedValues.Count == 0 ? "<none>" : string.Join(", ", materializedValues);
    }

    private static void AppendBulletedSection(StringBuilder builder, string title, IEnumerable<string?>? lines)
    {
        var materializedLines = lines?
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line!.Trim())
            .ToList();

        if (materializedLines == null || materializedLines.Count == 0)
        {
            return;
        }

        builder.AppendLine(title + ":");
        foreach (var line in materializedLines)
        {
            builder.Append("- ");
            builder.AppendLine(line);
        }
    }

    private static void AppendNumberedSection(StringBuilder builder, string? title, IEnumerable<string?>? lines)
    {
        var materializedLines = lines?
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line!.Trim())
            .ToList();

        if (string.IsNullOrWhiteSpace(title) || materializedLines == null || materializedLines.Count == 0)
        {
            return;
        }

        builder.AppendLine(title);
        for (var index = 0; index < materializedLines.Count; index++)
        {
            builder.Append(index + 1);
            builder.Append(". ");
            builder.AppendLine(materializedLines[index]);
        }
    }
}
