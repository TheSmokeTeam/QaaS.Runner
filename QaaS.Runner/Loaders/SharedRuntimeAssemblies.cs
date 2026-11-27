using System.Reflection;
using System.Runtime.InteropServices;

namespace QaaS.Runner.Loaders;

internal static class SharedRuntimeAssemblies
{
    private static readonly Lazy<Dictionary<string, Lazy<AssemblyName?>>> Installed = new(() =>
        FindTrustedFiles(
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string,
            RuntimeEnvironment.GetRuntimeDirectory(),
            AppContext.BaseDirectory
        )
    );

    internal static bool Provides(AssemblyName reference) =>
        Installed.Value.TryGetValue(reference.Name!, out var identity)
        && identity.Value is { } definition
        && CanBind(reference, definition);

    internal static bool CanBind(AssemblyName requested, AssemblyName definition) =>
        requested.Version != null
        && definition.Version >= requested.Version
        && string.Equals(definition.Name, requested.Name, StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            definition.CultureName,
            requested.CultureName,
            StringComparison.OrdinalIgnoreCase
        )
        && definition.ContentType == requested.ContentType
        && (requested.Flags & AssemblyNameFlags.Retargetable) == 0
        && (definition.GetPublicKeyToken() ?? [])
            .AsSpan()
            .SequenceEqual(requested.GetPublicKeyToken() ?? []);

    internal static Dictionary<string, Lazy<AssemblyName?>> FindTrustedFiles(
        string? trustedPaths,
        string runtimeDirectory,
        string applicationDirectory
    )
    {
        var result = new Dictionary<string, Lazy<AssemblyName?>>(StringComparer.OrdinalIgnoreCase);
        var directory = Path.TrimEndingDirectorySeparator(runtimeDirectory);
        var paths = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        // The runtime directory of a self-contained application can also contain its plugins.
        if (
            trustedPaths == null
            || paths.Equals(directory, Path.TrimEndingDirectorySeparator(applicationDirectory))
            || Path.GetFileName(Path.GetDirectoryName(directory)) != "Microsoft.NETCore.App"
        )
            return result;

        var entries = trustedPaths
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(paths)
            .ToLookup(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.Key is null || entry.Count() != 1)
                continue;
            var path = entry.First();
            if (paths.Equals(Path.GetDirectoryName(path), directory))
                result[entry.Key] = new Lazy<AssemblyName?>(() => ReadIdentity(path));
        }
        return result;
    }

    private static AssemblyName? ReadIdentity(string path)
    {
        try
        {
            if (new FileInfo(path).LinkTarget != null)
                return null;
            var identity = AssemblyName.GetAssemblyName(path);
            // Strong-name keys used by the Microsoft.NETCore.App framework and its facades.
            return
                Convert.ToHexString(identity.GetPublicKeyToken() ?? [])
                    is "7CEC85D7BEA7798E"
                        or "B03F5F7F11D50A3A"
                        or "B77A5C561934E089"
                        or "CC7B13FFCD2DDD51"
                ? identity
                : null;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or BadImageFormatException
                        or System.Security.SecurityException
            )
        {
            return null;
        }
    }
}
