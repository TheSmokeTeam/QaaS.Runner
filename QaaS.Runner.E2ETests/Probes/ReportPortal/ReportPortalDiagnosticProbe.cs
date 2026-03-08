using System.Collections.Immutable;
using System.Text;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.E2ETests.Probes.ReportPortal;

/// <summary>
/// Probe used by the ReportPortal E2E scenarios to create predictable session history, including recoverable session
/// failures when requested.
/// </summary>
public sealed class ReportPortalDiagnosticProbe : BaseProbe<ReportPortalDiagnosticProbeConfig>
{
    public override void Run(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        if (Configuration.DelayMilliseconds > 0)
            Thread.Sleep(Configuration.DelayMilliseconds);

        if (!Configuration.ThrowOnRun)
            return;

        var message = new StringBuilder()
            .Append("Probe `")
            .Append(Configuration.StepName)
            .Append("` failed on purpose for ReportPortal history testing. ")
            .Append(Configuration.TraceMessage);

        if (Configuration.Notes.Count > 0)
            message.Append(' ').Append(string.Join(' ', Configuration.Notes));

        throw new InvalidOperationException(message.ToString());
    }
}
