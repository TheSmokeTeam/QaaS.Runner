using System.Diagnostics.CodeAnalysis;
using QaaS.Framework.Configurations;
using QaaS.Framework.SDK.DataSourceObjects;

namespace QaaS.Runner;

public static class Constants
{
    /// <summary>
    /// Constant string names for useful qaas objects
    /// </summary>
    public const string DefaultQaasConfigurationFileName = "test.qaas.yaml",
        DefaultQaaSExecutionConfigurationFileName = "executable.yaml",
        DataSources = "DataSources",
        Sessions = "Sessions",
        Storages = "Storages",
        Assertions = "Assertions",
        Links = "Links";

    private const string ListPathSeparator =
            @$"{ConfigurationConstants.PathSeparator}\d+{ConfigurationConstants.PathSeparator}",
        EndOfListPath = @$"{ConfigurationConstants.PathSeparator}\d+";

    /// <summary>
    /// All configurations lists who are supported as references in the qaas configurations
    /// </summary>
    public static readonly IList<string> SupportedReferenceLists = new List<string>
    {
        DataSources,
        Sessions,
        Storages,
        Assertions,
        Links
    };

    /// <summary>
    /// All regex paths of the unique ids who are supported as references in the qaas configurations,
    /// the references add a prefix of the reference replace keyword to the values of the fields these regex's lead to
    /// in the qaas configurations
    /// </summary>
    public static readonly IList<string> SupportedUniqueIdsPathRegexes = new List<string>
    {
        // DataSources
        nameof(DataSources) + ListPathSeparator +
        nameof(DataSourceBuilder.Name),

        nameof(DataSources) + ListPathSeparator +
        nameof(DataSourceBuilder.DataSourceNames) + EndOfListPath,

        // Sessions
        nameof(Sessions) + ListPathSeparator + "Name",

        nameof(Sessions) + ListPathSeparator +
        "Publishers" + ListPathSeparator +
        "DataSourceNames" + EndOfListPath,

        nameof(Sessions) + ListPathSeparator +
        "Transactions" + ListPathSeparator +
        "DataSourceNames" + EndOfListPath,

        // Assertions
        nameof(Assertions) + ListPathSeparator + "Name",

        nameof(Assertions) + ListPathSeparator + "SessionNames" + EndOfListPath,

        nameof(Assertions) + ListPathSeparator + "DataSourceNames" + EndOfListPath
    };
}
