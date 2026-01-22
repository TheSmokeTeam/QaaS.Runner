namespace QaaS.Runner.Infrastructure;

/// <summary>
///     Contains extension functions related to file system functionality
/// </summary>
public static class FileSystemExtensions
{
    /// <summary>
    ///     Makes a string into a valid directory name according to current OS
    /// </summary>
    public static string? MakeValidDirectoryName(string? name)
    {
        return name == null
            ? name
            : new string(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
    }
}