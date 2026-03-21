using System.Collections.Generic;
using System.Collections.Immutable;
using Moq;
using NUnit.Framework;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace QaaS.Runner.Sessions.Tests.Actions.Probes;

public class ProbeTests
{
    private Mock<IProbe>? _hook;
    
    private Sessions.Actions.Probes.Probe CreateProbe(string[] dataSourceNames, string[] dataSourcePatterns,
        Microsoft.Extensions.Logging.ILogger? logger = null, string sessionName = "test-session")
    {
        _hook = new Mock<IProbe>();
        _hook.Setup(
            h => 
                h.Run(
                    It.IsAny<IImmutableList<SessionData>>(),
                    It.IsAny<IImmutableList<DataSource>>()
                    )
                );
        
        return new Sessions.Actions.Probes.Probe(
            "test probe", sessionName, 0, _hook.Object, dataSourceNames, dataSourcePatterns,
            logger ?? new SerilogLoggerFactory(new LoggerConfiguration().MinimumLevel
                .Is(LogEventLevel.Information).WriteTo.Console().CreateLogger()).CreateLogger("DefaultLogger"));
    }

    [Test,
     TestCaseSource(typeof(TestResourceDataSources),
         nameof(TestResourceDataSources.ValidDataSourceNamesAndAppropriateFilters))]
    public void
        TestActAndInitializeIterableSerializableSaveIterator_ReceivesValidDataSourceNamesAndPatternsAndAMockIProbe_CallsTheProbeWithTheAppropriateData(
            List<string> names,
            List<string> patterns,
            List<DataSource> dataSources,
            List<Data<object>> expectedData)
    {
        var probe = CreateProbe(names.ToArray(), patterns.ToArray());
        
        probe.InitializeIterableSerializableSaveIterator([], dataSources);
        probe.Act();
        
        _hook!.Verify(
            h =>
                h.Run(
                    It.IsAny<IImmutableList<SessionData>>(),
                    It.IsAny<IImmutableList<DataSource>>()), 
            Times.Exactly(1));
    }

    [Test]
    public void Constructor_LogsSessionScopedProbeInitializationMessage()
    {
        var logger = new CapturingLogger();

        _ = new Sessions.Actions.Probes.Probe(
            "SharedProbe",
            "session-a",
            0,
            new TestProbeHook(),
            [],
            [],
            logger);

        Assert.That(logger.Messages,
            Contains.Item("Initializing Probe SharedProbe for session session-a with Hook type TestProbeHook"));
    }

    private sealed class TestProbeHook : IProbe
    {
        public Context Context { get; set; } = null!;

        public List<System.ComponentModel.DataAnnotations.ValidationResult>? LoadAndValidateConfiguration(
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            return [];
        }

        public void Run(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
        {
        }
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NoOpScope.Instance;
        }

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NoOpScope : IDisposable
        {
            public static readonly NoOpScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
