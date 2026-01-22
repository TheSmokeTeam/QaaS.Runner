using System.ComponentModel;
using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using QaaS.Framework.Configurations.CommonConfigurationObjects;
using QaaS.Framework.SDK.ContextObjects;
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

    internal IStorage Build(Context context)
    {
        var storageType = new List<object?> { S3, FileSystem }.FirstOrDefault(storage => storage != null) ??
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