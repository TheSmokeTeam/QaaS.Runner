using QaaS.Framework.Configurations.CommonConfigurationObjects;
using QaaS.Runner.Infrastructure;
using S3Config = QaaS.Runner.Storage.ConfigurationObjects.StorageConfigs.S3Config;

namespace QaaS.Runner.Storage;

/// <summary>
///     Handles how to retrieve/store session items as part of a specific case for every type of storage
/// </summary>
public static class CaseStorageHandler
{
    /// <summary>
    ///     Creates the prefix of the session data items for an s3 bucket
    /// </summary>
    public static string HandleCaseWithS3(S3Config config, string? caseName)
    {
        return caseName == null ? config.Prefix : $"{config.Prefix}{caseName.Replace("/", "_").Replace("\\", "_")}/";
    }

    /// <summary>
    ///     Creates the path of the session data items for file system
    /// </summary>
    public static string HandleCaseWithFileSystem(FilesInFileSystemConfig config, string? caseName)
    {
        return caseName == null
            ? config.Path!
            : Path.Join(config.Path,
                FileSystemExtensions.MakeValidDirectoryName(caseName));
    }
}