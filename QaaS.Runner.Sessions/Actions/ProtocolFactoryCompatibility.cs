using System.Reflection;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Protocols.ConfigurationObjects;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.Protocols.Protocols.Factories;
using QaaS.Framework.SDK.Session;
using QaaS.Runner.Sessions.ConfigurationObjects;

namespace QaaS.Runner.Sessions.Actions;

internal static class ProtocolFactoryCompatibility
{
    private static readonly MethodInfo? ReaderFactoryWithTimeZoneMethod = typeof(ReaderFactory).GetMethod(
        nameof(ReaderFactory.CreateReader),
        [typeof(IReaderConfig), typeof(ILogger), typeof(DataFilter), typeof(string)]);

    private static readonly MethodInfo? SenderFactoryWithTimeZoneMethod = typeof(SenderFactory).GetMethod(
        nameof(SenderFactory.CreateSender),
        [typeof(bool), typeof(ISenderConfig), typeof(ILogger), typeof(DataFilter), typeof(string)]);

    internal static (IReader?, IChunkReader?) CreateReader(
        IReaderConfig configuration,
        ILogger logger,
        DataFilter? dataFilter,
        string timeZoneId)
    {
        if (ReaderFactoryWithTimeZoneMethod == null)
            return ReaderFactory.CreateReader(configuration, logger, dataFilter);

        return ((IReader?, IChunkReader?))ReaderFactoryWithTimeZoneMethod.Invoke(
            null,
            [configuration, logger, dataFilter, timeZoneId])!;
    }

    internal static (ISender?, IChunkSender?) CreateSender(
        bool isChunkable,
        ISenderConfig configuration,
        ILogger logger,
        DataFilter? dataFilter,
        string timeZoneId)
    {
        if (SenderFactoryWithTimeZoneMethod == null)
            return SenderFactory.CreateSender(isChunkable, configuration, logger, dataFilter);

        return ((ISender?, IChunkSender?))SenderFactoryWithTimeZoneMethod.Invoke(
            null,
            [isChunkable, configuration, logger, dataFilter, timeZoneId])!;
    }
}
