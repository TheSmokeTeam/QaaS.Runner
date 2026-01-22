using System.Collections.Generic;
using System.Collections.Immutable;
using Moq;
using NUnit.Framework;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace QaaS.Runner.Sessions.Tests.Actions.Probes;

public class ProbeTests
{
    private Mock<IProbe>? _hook;
    
    private Sessions.Actions.Probes.Probe CreateProbe(string[] dataSourceNames, string[] dataSourcePatterns)
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
            "test probe", 0, _hook.Object, dataSourceNames, dataSourcePatterns,
            new SerilogLoggerFactory(new LoggerConfiguration().MinimumLevel
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
}