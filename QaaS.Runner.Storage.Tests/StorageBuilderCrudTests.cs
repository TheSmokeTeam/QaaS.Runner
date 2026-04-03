using System.Reflection;
using NUnit.Framework;
using QaaS.Framework.Configurations.CommonConfigurationObjects;
using QaaS.Runner.Storage.ConfigurationObjects;
using QaaS.Runner.Storage.ConfigurationObjects.StorageConfigs;

namespace QaaS.Runner.Storage.Tests;

[TestFixture]
public class StorageBuilderCrudTests
{
    [Test]
    public void StorageBuilder_ShouldSupportConfigurationCrud()
    {
        var builder = new StorageBuilder()
            .Configure(new FilesInFileSystemConfig { Path = "one/path" });

        Assert.That(builder.Configuration, Is.TypeOf<FilesInFileSystemConfig>());

        builder.UpdateConfiguration(_ => new S3Config { Prefix = "prefix" });
        builder.UpdateConfiguration(new FilesInFileSystemConfig { Path = "two/path" });
        Assert.That(builder.Configuration, Is.TypeOf<FilesInFileSystemConfig>());

        builder.Configure(new S3Config { Prefix = "latest-prefix" });
        Assert.That(builder.Configuration, Is.TypeOf<S3Config>());
    }

    [Test]
    public void StorageBuilder_UpdateConfiguration_WithConfiguration_MergesSameTypeAndPreservesExistingFields()
    {
        var builder = new StorageBuilder()
            .Configure(new S3Config
            {
                StorageBucket = "bucket-a",
                ServiceURL = "https://s3.local",
                AccessKey = "access-key",
                SecretKey = "secret-key",
                Prefix = "existing-prefix",
                Delimiter = "/",
                SkipEmptyObjects = true
            });

        builder.UpdateConfiguration(new S3Config
        {
            MaximumRetryCount = 5,
            SkipEmptyObjects = false
        });

        var mergedConfiguration = (S3Config)builder.Configuration!;
        Assert.Multiple(() =>
        {
            Assert.That(mergedConfiguration.StorageBucket, Is.EqualTo("bucket-a"));
            Assert.That(mergedConfiguration.ServiceURL, Is.EqualTo("https://s3.local"));
            Assert.That(mergedConfiguration.Prefix, Is.EqualTo("existing-prefix"));
            Assert.That(mergedConfiguration.Delimiter, Is.EqualTo("/"));
            Assert.That(mergedConfiguration.MaximumRetryCount, Is.EqualTo(5));
            Assert.That(mergedConfiguration.SkipEmptyObjects, Is.False);
        });
    }

    [Test]
    public void Build_WhenMultipleStorageConfigurationsAreSet_ShouldThrowInvalidOperationException()
    {
        var builder = new StorageBuilder();
        SetInternalProperty(builder, "FileSystem", new FilesInFileSystemConfig { Path = "path" });
        SetInternalProperty(builder, "S3", new S3Config { Prefix = "prefix" });

        var exception = Assert.Throws<TargetInvocationException>(() => InvokeBuild(builder));
        Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(exception.InnerException!.Message, Does.Contain("Multiple configurations provided"));
    }

    private static IStorage InvokeBuild(StorageBuilder builder)
    {
        var buildMethod = typeof(StorageBuilder).GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (IStorage)buildMethod.Invoke(builder, [Globals.Context])!;
    }

    private static void SetInternalProperty(StorageBuilder builder, string propertyName, object? value)
    {
        var property = typeof(StorageBuilder).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        property.SetValue(builder, value);
    }
}
