using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using Serilog;
using Serilog.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace QaaS.Runner.Sessions.Tests;

public static class Globals
{
    public static readonly ILogger Logger = new SerilogLoggerFactory(
        new LoggerConfiguration().MinimumLevel.Warning()
            .CreateLogger()).CreateLogger("TestsLogger");

    private static readonly InternalContext Context = new()
    {
        Logger = Logger, RootConfiguration = new ConfigurationBuilder().Build(),
        InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>())
    };

    public static InternalContext GetContextWithMetadata()
    {
        Context.InsertValueIntoGlobalDictionary(Context.GetMetaDataPath(), new MetaDataConfig
        {
            Team = "Smoke",
            System = "QaaS"
        });
        return Context;
    }
}
