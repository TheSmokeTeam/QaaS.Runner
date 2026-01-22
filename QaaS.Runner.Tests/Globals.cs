using Microsoft.Extensions.Configuration;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using Serilog;
using Serilog.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace QaaS.Runner.Tests;

public static class Globals
{
    public static readonly ILogger Logger = new SerilogLoggerFactory(
        new LoggerConfiguration().MinimumLevel.Debug()
            .WriteTo.NUnitOutput()
            .CreateLogger()).CreateLogger("TestsLogger");

    private static readonly InternalContext Context = new()
    {
        Logger = Logger, RootConfiguration = new ConfigurationBuilder().Build(),
        InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>())
    };

    public static InternalContext GetContextWithMetadata()
    {
        Context.InsertValueIntoGlobalDictionary([nameof(MetaDataConfig)], new MetaDataConfig
        {
            Team = "REDA",
            System = "QaaS"
        });
        return Context;
    }
}