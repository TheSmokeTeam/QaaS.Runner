using System.Linq;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Runner.Infrastructure;

namespace QaaS.Runner.Tests.Extensions;

[TestFixture]
public class ContextMetadataExtensionsTests
{
    [Test]
    public void GetMetaDataOrDefault_WhenMetadataIsMissing_ReturnsAndStoresEmptyMetadata()
    {
        var context = CreateContext();
        var metadataKey = context.GetMetaDataPath().Last();

        var metadata = context.GetMetaDataOrDefault();

        Assert.That(metadata, Is.Not.Null);
        Assert.That(context.InternalGlobalDict[metadataKey], Is.SameAs(metadata));
    }

    [Test]
    public void GetMetaDataOrDefault_WhenMetadataAlreadyExists_ReturnsConfiguredInstance()
    {
        var context = CreateContext();
        var configuredMetadata = new MetaDataConfig
        {
            Team = "Smoke",
            System = "QaaS"
        };
        context.InsertValueIntoGlobalDictionary(context.GetMetaDataPath(), configuredMetadata);

        var metadata = context.GetMetaDataOrDefault();

        Assert.That(metadata, Is.SameAs(configuredMetadata));
    }

    [Test]
    public void GetMetaDataOrDefault_WhenMetadataHasUnexpectedType_ReplacesIt()
    {
        var context = CreateContext();
        var metadataKey = context.GetMetaDataPath().Last();
        context.InternalGlobalDict[metadataKey] = "invalid";

        var metadata = context.GetMetaDataOrDefault();

        Assert.That(metadata, Is.TypeOf<MetaDataConfig>());
        Assert.That(context.InternalGlobalDict[metadataKey], Is.SameAs(metadata));
    }

    private static InternalContext CreateContext()
    {
        return new InternalContext
        {
            Logger = Globals.Logger,
            RootConfiguration = new ConfigurationBuilder().Build(),
            InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>()),
            InternalGlobalDict = new Dictionary<string, object?>()
        };
    }
}
