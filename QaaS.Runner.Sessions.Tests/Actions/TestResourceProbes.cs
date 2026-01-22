using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.Sessions.Tests.Actions;

public class TestResourceProbes
{
    
}

internal class TestProbe : BaseProbe<object>
{
    public override void Run(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        Context.Logger.LogDebug("Probe ran successfully");
    }
}