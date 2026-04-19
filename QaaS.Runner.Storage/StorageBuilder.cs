using System.ComponentModel;
using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using QaaS.Framework.Configurations;
using QaaS.Framework.Configurations.CommonConfigurationObjects;
using QaaS.Framework.Infrastructure;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Runner.Storage.ConfigurationObjects;
using QaaS.Runner.Storage.ConfigurationObjects.StorageConfigs;

[assembly: InternalsVisibleTo("QaaS.Runner")]

namespace QaaS.Runner.Storage;

/// <summary>
/// Builder class for IStorage implementations - using Configured properties for constructing the builders.
/// </summary>
public class StorageBuilder : ICloneable<StorageBuilder>
{
    public StorageBuilder Clone() => BuilderCloner.DeepClone(this);

    [Description("The storage format used when storing jsons. Options: " +
                 "[`Indented` - Formats the json with indents, more readable but less memory efficient /" +
                 "`None` - Formats the json without indents, less readable but more memory efficient ]")]
    [DefaultValue(Formatting.Indented)]
    public Formatting JsonStorageFormat { get; internal set; } = Formatting.Indented;
    [Description("Supports storage as a file system directory")]
    public FilesInFileSystemConfig? FileSystem { get; internal set; }
    [Description("Supports storage as an S3 bucket with a certain prefix")]
    public S3Config? S3 { get; internal set; }
    public IStorageConfig? Configuration
    {
        get => (IStorageConfig?)S3 ?? FileSystem;
        internal set
        {
            if (value == null)
            {
                Reset();
                return;
            }

            Configure(value);
        }
    }
    private StorageBuilder Reset()
    {
        FileSystem = null;
        S3 = null;
        return this;
    }

    /// <summary>
    /// Sets the JSON formatting used by runtime storages.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner storage builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Storages" />
    public StorageBuilder WithJsonStorageFormat(Formatting format)
    {
        JsonStorageFormat = format;
        return this;
    }

    /// <summary>
    /// Updates the configuration currently stored on the Runner storage builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner storage builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Storages" />
    public StorageBuilder UpdateConfiguration(object configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var currentConfig = Configuration;
        if (configuration is IStorageConfig typedConfiguration)
        {
            return Configure(currentConfig == null
                ? typedConfiguration
                : currentConfig.UpdateConfiguration(typedConfiguration));
        }

        if (currentConfig == null)
            throw new InvalidOperationException(
                "Storage configuration is not set and cannot be inferred from an object patch. Configure a concrete storage configuration first.");
        return Configure(currentConfig.UpdateConfiguration(configuration));
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner storage builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner storage builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Storages" />
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
    /// Returns the configuration currently stored on the Runner storage builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner storage builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Storages" />
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
