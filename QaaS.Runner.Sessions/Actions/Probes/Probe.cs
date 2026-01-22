using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.ConfigurationObjectFilters;
using QaaS.Framework.SDK.ConfigurationObjects;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.Sessions.Actions.Probes;

public class Probe : StagedAction
{
    private readonly string[]? _dataSourceNames;
    private readonly string[]? _dataSourcePatterns;

    public Probe(string name, int stage, IProbe probeHook, string[] dataSourceNames, string[] dataSourcePatterns,
        ILogger logger) : base(name, stage, null, logger)
    {
        ProbeHook = probeHook;
        _dataSourceNames = dataSourceNames;
        _dataSourcePatterns = dataSourcePatterns;
        Logger.LogInformation("Initializing Probe {Name} of type {ProbeType}", Name, ProbeHook.GetType());
    }

    public List<DataSource> DataSources { get; set; }
    public List<SessionData> SessionDataList { get; set; }

    public IProbe ProbeHook { get; set; }

    internal override InternalCommunicationData<object> Act()
    {
        Logger.LogDebug("Acting probe action {ActionName} ", Name);
        ProbeHook.Run(SessionDataList.ToImmutableList(), DataSources.ToImmutableList());
        // probe doesn't initialize either input and output
        return new InternalCommunicationData<object>();
    }

    internal override void ExportRunningCommunicationData(InternalContext context, string sessionName)
    {
    }

    protected internal override void LogData(InternalCommunicationData<object> actData,
        DetailedData<object> itemBeforeSerialization, InputOutputState? saveData = null)
    {
    }

    public void InitializeIterableSerializableSaveIterator(List<SessionData?> ranSessions, List<DataSource> dataSources)
    {
        DataSources = EnumerableExtensions.GetFilteredConfigurationObjectList(dataSources.ToImmutableList(),
                _dataSourcePatterns,
                RegexFilters.DataSource,
                "DataSource List")
            .Union(EnumerableExtensions.GetFilteredConfigurationObjectList(dataSources.ToImmutableList(),
                _dataSourceNames,
                NameFilters.DataSource,
                "DataSource List")).ToList();
        SessionDataList = ranSessions.Where(sessionData => sessionData != null).ToList()!;
    }
}