using System.Diagnostics.CodeAnalysis;

namespace QaaS.Runner.Infrastructure;

/// <summary>
/// Centralizes runner-wide constants shared by multiple runner packages.
/// </summary>
[ExcludeFromCodeCoverage]
public static class Constants
{
    /// <summary>
    /// Canonical top-level configuration section order used when rendering runner YAML output.
    /// </summary>
    public static readonly List<string> ConfigurationSectionNames =
    [
        "Storages",
        "DataSources",
        "Sessions",
        "Assertions",
        "Links",
        "MetaData"
    ];
}
