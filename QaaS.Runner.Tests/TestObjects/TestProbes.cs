using System.Collections.Concurrent;
using System.Collections.Immutable;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.Tests.TestObjects;

public sealed class ProbeMarkerConfig
{
    public string Marker { get; set; } = string.Empty;
}

public static class ProbeRunRecorder
{
    private static readonly ConcurrentQueue<(string ProbeName, string Marker)> Runs = new();
    private static readonly ConcurrentQueue<(string SessionName, string ProbeName)> ScopedRuns = new();

    public static IReadOnlyCollection<(string ProbeName, string Marker)> GetRuns()
    {
        return Runs.ToArray();
    }

    public static void Reset()
    {
        while (Runs.TryDequeue(out _))
        {
        }

        while (ScopedRuns.TryDequeue(out _))
        {
        }
    }

    public static void Record(string probeName, string marker)
    {
        Runs.Enqueue((probeName, marker));
    }

    public static IReadOnlyCollection<(string SessionName, string ProbeName)> GetScopedRuns()
    {
        return ScopedRuns.ToArray();
    }

    public static void RecordScope(string sessionName, string probeName)
    {
        ScopedRuns.Enqueue((sessionName, probeName));
    }
}

public class FirstTestProbe : BaseProbe<ProbeMarkerConfig>
{
    public override void Run(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        ProbeRunRecorder.Record(nameof(FirstTestProbe), Configuration.Marker);
    }
}

public class SecondTestProbe : BaseProbe<ProbeMarkerConfig>
{
    public override void Run(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        ProbeRunRecorder.Record(nameof(SecondTestProbe), Configuration.Marker);
    }
}

public class ScopeAwareTestProbe : BaseProbe<ProbeMarkerConfig>
{
    public override void Run(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        var descriptor = ProbeExecutionContext.GetCurrent(Context);
        ProbeRunRecorder.RecordScope(descriptor.SessionName, descriptor.ProbeName);
    }
}
