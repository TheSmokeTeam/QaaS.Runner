using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Infrastructure;
using QaaS.Runner.Storage.ConfigurationObjects;

namespace QaaS.Runner.Storage;

public abstract class BaseStorage : IStorage
{
    private readonly Formatting jsonStorageFormat;

    protected BaseStorage(Formatting jsonStorageFormat)
    {
        this.jsonStorageFormat = jsonStorageFormat;
    }

    public Context _context { get; set; } = default!;

    public void Store(ImmutableList<SessionData?>? sessionDataList, string? caseName)
    {
        var logger = _context?.Logger;
        var sessionsToStore = (sessionDataList ?? []).Where(sessionData => sessionData is not null)
            .Select(sessionData => sessionData!)
            .ToList();
        logger?.LogDebug("Preparing {SessionCount} session item(s) for storage in case {CaseName}",
            sessionsToStore.Count, caseName);
        var invalidNames = sessionsToStore
            .Where(sessionData => string.IsNullOrWhiteSpace(sessionData.Name))
            .Select(sessionData => sessionData.Name)
            .ToList();

        if (invalidNames.Count != 0)
            throw new InvalidOperationException("Session data names must be set before storing.");

        var serializedSessionDataList = sessionsToStore.Select(sessionData => new KeyValuePair<string, byte[]>(
                BuildStorageFileName(sessionData.Name!),
                SessionDataSerialization.SerializeSessionData(sessionData, new JsonSerializerOptions
                {
                    WriteIndented = jsonStorageFormat == Formatting.Indented,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                })))
            .ToList();

        var duplicateFileNames = serializedSessionDataList
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateFileNames.Count != 0)
        {
            throw new InvalidOperationException(
                "Multiple session data entries resolve to the same storage file name: " +
                string.Join(", ", duplicateFileNames));
        }

        logger?.LogDebug("Serialized {SessionCount} session item(s) for storage using format {Formatting}",
            serializedSessionDataList.Count, jsonStorageFormat);
        StoreSerialized(serializedSessionDataList, caseName);
    }

    public ImmutableList<SessionData> Retrieve(string? caseName)
    {
        var logger = _context?.Logger;
        var retrievedSessions = RetrieveSerialized(caseName)
            .Select(serializedSessionData =>
                SessionDataSerialization.DeserializeSessionData(serializedSessionData,
                    new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }))
            .ToImmutableList();
        logger?.LogDebug("Deserialized {SessionCount} session item(s) retrieved from storage for case {CaseName}",
            retrievedSessions.Count, caseName);
        return retrievedSessions;
    }

    protected abstract void StoreSerialized(
        IList<KeyValuePair<string, byte[]>> sessionFileNameAndSerializedSessionDataItemsToStorePair, string? caseName);

    protected abstract IEnumerable<byte[]> RetrieveSerialized(string? caseName);

    internal static string BuildStorageFileName(string sessionName)
    {
        return $"{FileSystemExtensions.MakeValidFileName(sessionName)}.json";
    }
}
