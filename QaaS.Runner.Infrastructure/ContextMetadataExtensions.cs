using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;

namespace QaaS.Runner.Infrastructure;

/// <summary>
/// Metadata helpers that keep runner flows operational when the YAML omits the MetaData section.
/// </summary>
public static class ContextMetadataExtensions
{
    /// <summary>
    /// Returns configured metadata when present; otherwise creates and stores an empty metadata object.
    /// </summary>
    public static MetaDataConfig GetMetaDataOrDefault(this InternalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.InternalGlobalDict ??= new Dictionary<string, object?>();
        var metadataPath = context.GetMetaDataPath();
        var metadataKey = metadataPath.Last();

        if (context.InternalGlobalDict.TryGetValue(metadataKey, out var configuredMetaData) &&
            configuredMetaData is MetaDataConfig metaDataConfig)
        {
            return metaDataConfig;
        }

        if (context.InternalGlobalDict.TryGetValue(metadataKey, out configuredMetaData) &&
            configuredMetaData is not null)
        {
            context.Logger.LogWarning(
                "MetaData entry at path {MetaDataPath} had unexpected type {MetaDataType}; replacing it with an empty configuration.",
                metadataPath, configuredMetaData.GetType().FullName);
        }
        else
        {
            context.Logger.LogDebug("MetaData was not configured. Using an empty metadata object.");
        }

        var fallbackMetaData = new MetaDataConfig();
        context.InsertValueIntoGlobalDictionary(metadataPath, fallbackMetaData);
        return fallbackMetaData;
    }
}
