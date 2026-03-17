using Microsoft.Extensions.Configuration;
using QaaS.Framework.SDK.ContextObjects;
using Serilog;
using Serilog.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace QaaS.Runner.Storage.Tests;

public static class Globals
{
    public static readonly ILogger Logger = new SerilogLoggerFactory(
        new LoggerConfiguration().MinimumLevel.Warning()
            .CreateLogger()).CreateLogger("TestsLogger");

    public static readonly Context Context = new()
    {
        Logger = Logger,
        RootConfiguration = new ConfigurationBuilder().Build()
    };
}
