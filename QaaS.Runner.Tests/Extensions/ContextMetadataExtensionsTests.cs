using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
        var metadataPath = context.GetMetaDataPath();

        var metadata = context.GetMetaDataOrDefault();

        Assert.That(metadata, Is.Not.Null);
        Assert.That(context.GetValueFromGlobalDictionary(metadataPath), Is.SameAs(metadata));
    }

    [Test]
    public void GetMetaDataOrDefault_WhenMetadataAlreadyExists_ReturnsConfiguredInstance()
    {
        var context = CreateContext(caseName: "case-a", executionId: "execution-1");
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
        var context = CreateContext(caseName: "case-a", executionId: "execution-1");
        var metadataPath = context.GetMetaDataPath();
        context.InsertValueIntoGlobalDictionary(metadataPath, "invalid");

        var metadata = context.GetMetaDataOrDefault();

        Assert.That(metadata, Is.TypeOf<MetaDataConfig>());
        Assert.That(context.GetValueFromGlobalDictionary(metadataPath), Is.SameAs(metadata));
    }

    [Test]
    public void GetMetaDataOrDefault_WhenMetadataIsMissingAndRequestedRepeatedly_LogsFallbackOnlyOnce()
    {
        var logger = new CapturingLogger();
        var context = CreateContext(logger);

        var firstMetadata = context.GetMetaDataOrDefault();
        var secondMetadata = context.GetMetaDataOrDefault();

        Assert.Multiple(() =>
        {
            Assert.That(secondMetadata, Is.SameAs(firstMetadata));
            Assert.That(logger.Entries.Count(entry =>
                    entry.LogLevel == LogLevel.Debug &&
                    entry.Message.Contains("MetaData was not configured", StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(logger.Entries.Count(entry => entry.LogLevel == LogLevel.Warning), Is.EqualTo(0));
        });
    }

    private static InternalContext CreateContext(ILogger? logger = null, string? caseName = null,
        string? executionId = null)
    {
        return new InternalContext
        {
            Logger = logger ?? Globals.Logger,
            RootConfiguration = new ConfigurationBuilder().Build(),
            CaseName = caseName,
            ExecutionId = executionId,
            InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>()),
            InternalGlobalDict = new Dictionary<string, object?>()
        };
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NoOpScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        private sealed class NoOpScope : IDisposable
        {
            public static readonly NoOpScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, string Message);
}
