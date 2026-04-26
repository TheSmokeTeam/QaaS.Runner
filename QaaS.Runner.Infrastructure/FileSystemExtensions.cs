namespace QaaS.Runner.Infrastructure;

/// <summary>
/// Shared path-safety helpers used by runner components that persist files and attachments.
/// </summary>
public static class FileSystemExtensions
{
    private static readonly char[] InvalidPathSegmentChars =
        Path.GetInvalidFileNameChars()
            .Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*'])
            .Distinct()
            .ToArray();

    /// <summary>
    /// Sanitizes a value so it can safely be used as a single directory name segment on the current OS.
    /// </summary>
    public static string? MakeValidDirectoryName(string? name)
    {
        return MakeValidPathSegment(name);
    }

    /// <summary>
    /// Sanitizes a value so it can safely be used as a single file name segment on the current OS.
    /// </summary>
    public static string? MakeValidFileName(string? name)
    {
        return MakeValidPathSegment(name);
    }

    /// <summary>
    /// Normalizes a relative path while rejecting rooted paths and traversal segments such as <c>..</c>.
    /// </summary>
    public static string NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        if (Path.IsPathRooted(path))
            throw new InvalidOperationException($"Path '{path}' must be relative.");

        var segments = path.Split(['/', '\\'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Any(segment => segment is "." or ".."))
            throw new InvalidOperationException($"Path '{path}' contains traversal segments.");

        var sanitizedSegments = segments
            .Select(segment => MakeValidPathSegment(segment) ?? throw new InvalidOperationException(
                $"Path segment '{segment}' is invalid."))
            .ToArray();

        return sanitizedSegments.Length == 0 ? string.Empty : Path.Join(sanitizedSegments);
    }

    /// <summary>
    /// Combines path segments under a fixed root and throws when the resolved path escapes that root.
    /// </summary>
    public static string CombineUnderRoot(string rootPath, params string?[] segments)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));

        var fullRootPath = Path.GetFullPath(rootPath);
        var combinedPath = fullRootPath;
        foreach (var segment in segments.Where(segment => !string.IsNullOrWhiteSpace(segment)).Cast<string>())
        {
            combinedPath = Path.Combine(combinedPath, segment);
        }
        var fullCombinedPath = Path.GetFullPath(combinedPath);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedRootPath = fullRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!string.Equals(fullCombinedPath, normalizedRootPath, comparison) &&
            !fullCombinedPath.StartsWith(normalizedRootPath + Path.DirectorySeparatorChar, comparison) &&
            !fullCombinedPath.StartsWith(normalizedRootPath + Path.AltDirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException(
                $"Resolved path '{fullCombinedPath}' escapes configured root '{fullRootPath}'.");
        }

        return fullCombinedPath;
    }

    private static string? MakeValidPathSegment(string? name)
    {
        if (name == null)
            return null;

        if (name.Length == 0)
            return name;

        var sanitized = new string(name.Select(ch => InvalidPathSegmentChars.Contains(ch) ? '_' : ch).ToArray())
            .TrimEnd('.', ' ');

        if (sanitized.Length == 0)
            return "_";

        return sanitized is "." or ".."
            ? sanitized.Replace('.', '_')
            : sanitized;
    }
}
