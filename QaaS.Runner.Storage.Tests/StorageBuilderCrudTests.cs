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
            .Create(new FilesInFileSystemConfig { Path = "one/path" });

        Assert.That(builder.ReadConfiguration(), Is.TypeOf<FilesInFileSystemConfig>());

        builder.UpdateConfiguration(_ => new S3Config { Prefix = "prefix" });
        builder.UpsertConfiguration(new FilesInFileSystemConfig { Path = "two/path" });
        Assert.That(builder.ReadConfiguration(), Is.TypeOf<FilesInFileSystemConfig>());

        builder.DeleteConfiguration();
        Assert.That(builder.ReadConfiguration(), Is.Null);
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
        var property = typeof(StorageBuilder).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        property.SetValue(builder, value);
    }
}
