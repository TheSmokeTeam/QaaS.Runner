using System.Collections.Concurrent;
using System.Collections.Immutable;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.E2ETests.Probes;

public sealed class ProbeMarkerConfig
{
    public string Marker { get; set; } = string.Empty;
}

public static class ProbeRunRecorder
{
    private static readonly ConcurrentQueue<string> Markers = new();

    public static IReadOnlyCollection<string> GetMarkers()
    {
        return Markers.ToArray();
    }

    public static void Record(string marker)
    {
        Markers.Enqueue(marker);
    }
}

public class ScopedProbe : BaseProbe<ProbeMarkerConfig>
{
    public override void Run(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        ProbeRunRecorder.Record(Configuration.Marker);
    }
}
