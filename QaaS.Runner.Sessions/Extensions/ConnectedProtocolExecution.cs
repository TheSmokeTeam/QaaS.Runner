using Microsoft.Extensions.Logging;
using QaaS.Framework.Protocols.Protocols;

namespace QaaS.Runner.Sessions.Extensions;

internal static class ConnectedProtocolExecution
{
    internal static T Run<T>(IConnectable? protocol, Func<T> action, ILogger logger)
    {
        protocol?.Connect();
        var completed = false;
        try
        {
            var result = action();
            completed = true;
            return result;
        }
        finally
        {
            if (completed)
                protocol?.Disconnect(); // A failed flush must fail an otherwise successful action.
            else
            {
                try
                {
                    protocol?.Disconnect();
                }
                catch (Exception cleanupError)
                {
                    logger.LogWarning(
                        cleanupError,
                        "Protocol disconnect also failed after an action failure."
                    );
                }
            }
        }
    }
}
