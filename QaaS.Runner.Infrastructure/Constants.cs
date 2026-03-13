using System.Diagnostics.CodeAnalysis;

namespace QaaS.Runner.Infrastructure;

/// <summary>
/// Useful constants class for all Runner's infra projects
/// </summary>
[ExcludeFromCodeCoverage]
public static class Constants
{
    /// <summary>
    /// List of known names for all QaaS Runner's configurations sections
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
