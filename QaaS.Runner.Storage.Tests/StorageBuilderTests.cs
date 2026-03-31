using System.Reflection;
using NUnit.Framework;
using QaaS.Framework.Configurations.CommonConfigurationObjects;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Runner.Storage.ConfigurationObjects;
using S3Config = QaaS.Runner.Storage.ConfigurationObjects.StorageConfigs.S3Config;

namespace QaaS.Runner.Storage.Tests;

[TestFixture]
public class StorageBuilderTests
{
    [Test]
    public void Build_WithoutConfiguredStorage_ThrowsInvalidOperationException()
    {
        var builder = new StorageBuilder();

        var exception = Assert.Throws<TargetInvocationException>(() => InvokeBuild(builder, Globals.Context));
        Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void Build_WithFileSystemConfiguration_ReturnsFileSystemStorageAndSetsContext()
    {
        var builder = new StorageBuilder()
            .Configure(new FilesInFileSystemConfig { Path = "some/path" });

        var storage = InvokeBuild(builder, Globals.Context);

        Assert.That(storage, Is.TypeOf<FileSystemStorage>());
        Assert.That(((BaseStorage)storage)._context, Is.SameAs(Globals.Context));
    }

    [Test]
    public void Build_WithS3Configuration_ReturnsS3StorageAndSetsContext()
    {
        var builder = new StorageBuilder()
            .Configure(new S3Config());

        var storage = InvokeBuild(builder, Globals.Context);

        Assert.That(storage, Is.TypeOf<S3Storage>());
        Assert.That(((BaseStorage)storage)._context, Is.SameAs(Globals.Context));
    }

    [Test]
    public void Configure_WhenCalledMultipleTimes_ResetsPreviousConfiguration()
    {
        var builder = new StorageBuilder()
            .Configure(new S3Config())
            .Configure(new FilesInFileSystemConfig { Path = "some/path" });

        var storage = InvokeBuild(builder, Globals.Context);

        Assert.That(storage, Is.TypeOf<FileSystemStorage>());
    }

    [Test]
    public void WithJsonStorageFormat_ReturnsSameBuilderInstance()
    {
        var builder = new StorageBuilder();

        var returnedBuilder = builder.WithJsonStorageFormat(Formatting.None);

        Assert.That(returnedBuilder, Is.SameAs(builder));
    }

    [Test]
    public void UpdateConfiguration_WithoutExistingConfiguration_ThrowsInvalidOperationException()
    {
        var builder = new StorageBuilder();

        Assert.Throws<InvalidOperationException>(() =>
            builder.UpdateConfiguration(config => config));
    }

    [Test]
    public void UpdateConfiguration_WithConfigurationWithoutExistingConfiguration_ThrowsInvalidOperationException()
    {
        var builder = new StorageBuilder();

        Assert.Throws<InvalidOperationException>(() =>
            builder.UpdateConfiguration(new FilesInFileSystemConfig { Path = "some/path" }));
    }

    private static IStorage InvokeBuild(StorageBuilder builder, Context context)
    {
        var buildMethod = typeof(StorageBuilder).GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(buildMethod, Is.Not.Null);
        return (IStorage)buildMethod!.Invoke(builder, [context])!;
    }
}

