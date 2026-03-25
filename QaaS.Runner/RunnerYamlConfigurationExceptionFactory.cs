using System.Text.RegularExpressions;
using QaaS.Framework.Configurations.CustomExceptions;

namespace QaaS.Runner;

internal static partial class RunnerYamlConfigurationExceptionFactory
{
    public static bool ShouldWrap(Exception exception)
    {
        if (exception is CouldNotFindConfigurationException or InvalidConfigurationsException or FileNotFoundException
            or DirectoryNotFoundException)
        {
            return true;
        }

        for (var current = exception; current != null; current = current.InnerException)
        {
            if (LoadedFilePathRegex().IsMatch(current.Message) ||
                ParserLocationRegex().IsMatch(current.Message) ||
                YamlFormatRegex().IsMatch(current.Message))
            {
                return true;
            }
        }

        return false;
    }

    public static Exception CreateLocalFileLoadException(string configuredPath, Exception exception)
    {
        if (exception is CouldNotFindConfigurationException or InvalidConfigurationsException)
        {
            return exception;
        }

        var resolvedPath = TryExtractResolvedPath(exception) ?? ResolveLocalPath(configuredPath);

        if (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new CouldNotFindConfigurationException(
                RunnerDiagnosticMessageFormatter.Format(
                    "YAML configuration file was not found.",
                    [$"Resolved local path: {resolvedPath}"],
                    null,
                    null,
                    ["Provide a valid YAML file path and retry."]),
                exception);
        }

        if (TryGetParserDiagnostics(exception, out var parserLocation, out var parserDetail))
        {
            return new InvalidConfigurationsException(
                RunnerDiagnosticMessageFormatter.Format(
                    "YAML configuration file is invalid and QaaS cannot continue.",
                    [
                        $"Resolved local path: {resolvedPath}",
                        parserLocation,
                        $"Parser detail: {parserDetail}"
                    ],
                    null,
                    null,
                    [
                        "Fix the YAML syntax at the reported file and location, then retry.",
                        "Parser locations are 1-based line and column numbers when available."
                    ]));
        }

        return new InvalidConfigurationsException(
            RunnerDiagnosticMessageFormatter.Format(
                "YAML configuration file could not be loaded.",
                [
                    $"Resolved local path: {resolvedPath}",
                    $"Load failure detail: {GetMostRelevantMessage(exception)}"
                ],
                null,
                null,
                ["Fix the file contents or accessibility issue and retry."]));
    }

    private static string ResolveLocalPath(string configuredPath)
    {
        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, configuredPath));
    }

    private static string? TryExtractResolvedPath(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is FileNotFoundException fileNotFoundException &&
                !string.IsNullOrWhiteSpace(fileNotFoundException.FileName))
            {
                return fileNotFoundException.FileName;
            }

            var match = LoadedFilePathRegex().Match(current.Message);
            if (match.Success)
            {
                return match.Groups["path"].Value;
            }
        }

        return null;
    }

    private static bool TryGetParserDiagnostics(
        Exception exception,
        out string? parserLocation,
        out string parserDetail)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            var locationMatch = ParserLocationRegex().Match(current.Message);
            if (locationMatch.Success)
            {
                parserLocation =
                    $"Parser location: line {locationMatch.Groups["line"].Value}, column {locationMatch.Groups["column"].Value}";
                parserDetail = locationMatch.Groups["detail"].Value.Trim().TrimEnd('.');
                return true;
            }

            var formatMatch = YamlFormatRegex().Match(current.Message);
            if (formatMatch.Success)
            {
                parserLocation = null;
                parserDetail = formatMatch.Groups["detail"].Value.Trim().TrimEnd('.');
                return true;
            }
        }

        parserLocation = null;
        parserDetail = string.Empty;
        return false;
    }

    private static string GetMostRelevantMessage(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                return current.Message.Trim();
            }
        }

        return exception.GetType().Name;
    }

    [GeneratedRegex(@"Failed to load configuration from file '(?<path>[^']+)'\.", RegexOptions.Compiled)]
    private static partial Regex LoadedFilePathRegex();

    [GeneratedRegex(@"\(Line:\s*(?<line>\d+),\s*Col:\s*(?<column>\d+),.*?\):\s*(?<detail>.+)$",
        RegexOptions.Compiled)]
    private static partial Regex ParserLocationRegex();

    [GeneratedRegex(@"Could not parse the YAML file:\s*(?<detail>.+?)(?:\.\s*)?$", RegexOptions.Compiled)]
    private static partial Regex YamlFormatRegex();
}
