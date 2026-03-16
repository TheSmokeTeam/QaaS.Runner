using System.ComponentModel;
using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using QaaS.Framework.Configurations.CommonConfigurationObjects;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Runner.Infrastructure;
using QaaS.Runner.Storage.ConfigurationObjects;
using QaaS.Runner.Storage.ConfigurationObjects.StorageConfigs;

[assembly: InternalsVisibleTo("QaaS.Runner")]

namespace QaaS.Runner.Storage;

/// <summary>
/// Builder class for IStorage implementations - using Configured properties for constructing the builders.
/// </summary>
public class StorageBuilder
{
    [Description("The storage format used when storing jsons. Options: " +
                 "[`Indented` - Formats the json with indents, more readable but less memory efficient /" +
                 "`None` - Formats the json without indents, less readable but more memory efficient ]")]
    [DefaultValue(Formatting.Indented)]
    internal Formatting JsonStorageFormat { get; set; } = Formatting.Indented;

    [Description("Supports storage as a file system directory")]
    internal FilesInFileSystemConfig? FileSystem { get; set; }

    [Description("Supports storage as an S3 bucket with a certain prefix")]
    internal S3Config? S3 { get; set; }

    private StorageBuilder Reset()
    {
        FileSystem = null;
        S3 = null;
        return this;
    }

    public StorageBuilder WithJsonStorageFormat(Formatting format)
    {
        JsonStorageFormat = format;
        return this;
    }

    public StorageBuilder Create(IStorageConfig storageConfig)
    {
        return Configure(storageConfig);
    }

    public IStorageConfig? ReadConfiguration()
    {
        return (IStorageConfig?)S3 ?? FileSystem;
    }

    /// <summary>
    /// Applies a computed partial update to the current storage configuration while preserving omitted fields.
    /// </summary>
    public StorageBuilder UpdateConfiguration(Func<IStorageConfig, IStorageConfig> update)
    {
        var currentConfig = ReadConfiguration() ??
                            throw new InvalidOperationException("Storage configuration is not set");
        return UpdateConfiguration(update(currentConfig));
    }

    /// <summary>
    /// Updates the storage configuration by merging same-type values and replacing the current type when needed.
    /// </summary>
    public StorageBuilder UpdateConfiguration(IStorageConfig storageConfig)
    {
        return Configure(ReadConfiguration().UpdateConfiguration(storageConfig));
    }

    public StorageBuilder DeleteConfiguration()
    {
        Reset();
        return this;
    }

    public StorageBuilder Configure(IStorageConfig storageConfig)
    {
        Reset();
        switch (storageConfig)
        {
            case FilesInFileSystemConfig filesInFileSystemConfig:
                FileSystem = filesInFileSystemConfig;
                break;
            case S3Config s3Config:
                S3 = s3Config;
                break;
        }

        return this;
    }

    /// <summary>
    /// Builds a runtime storage instance from the configured type and wires the execution context into it.
    /// </summary>
    internal IStorage Build(Context context)
    {
        var configuredStorages = new List<object?> { S3, FileSystem };
        if (configuredStorages.Count(storage => storage != null) > 1)
        {
            var conflictingConfigs = configuredStorages
                .Where(storage => storage != null)
                .Select(storage => storage!.GetType().Name)
                .ToArray();
            throw new InvalidOperationException(
                $"Multiple configurations provided for Storage: {string.Join(", ", conflictingConfigs)}. " +
                "Only one type is allowed at a time.");
        }

        var storageType = configuredStorages.FirstOrDefault(storage => storage != null) ??
                          throw new InvalidOperationException("Missing supported type for storage");
        BaseStorage storage = storageType! switch
        {
            S3Config => new S3Storage(S3!, JsonStorageFormat),
            FilesInFileSystemConfig => new FileSystemStorage(FileSystem!, new FileSystem(), JsonStorageFormat),
            _ => throw new ArgumentOutOfRangeException(nameof(storageType), storageType, "Storage not supported")
        };
        storage._context = context;
        return storage;
    }
}
