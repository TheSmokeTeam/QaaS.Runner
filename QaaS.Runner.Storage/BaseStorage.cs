using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Storage.ConfigurationObjects;

namespace QaaS.Runner.Storage;

public abstract class BaseStorage : IStorage
{
    private readonly Formatting jsonStorageFormat;

    protected BaseStorage(Formatting jsonStorageFormat)
    {
        this.jsonStorageFormat = jsonStorageFormat;
    }

    public Context _context { get; set; }

    public void Store(ImmutableList<SessionData?>? sessionDataList, string? caseName)
    {
        var serializedSessionDataList = sessionDataList?.Where(sessionData =>
                sessionData is not null).Select(sessionData => new KeyValuePair<string, byte[]>(
                $"{sessionData!.Name}.json",
                SessionDataSerialization.SerializeSessionData(sessionData, new JsonSerializerOptions
                {
                    WriteIndented = jsonStorageFormat == Formatting.Indented,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                })))
            .ToList();
        StoreSerialized(serializedSessionDataList ?? [], caseName);
    }

    public ImmutableList<SessionData> Retrieve(string? caseName)
    {
        return RetrieveSerialized(caseName)
            .Select(serializedSessionData =>
                SessionDataSerialization.DeserializeSessionData(serializedSessionData,
                    new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }))
            .ToImmutableList();
    }

    protected abstract void StoreSerialized(
        IList<KeyValuePair<string, byte[]>> sessionFileNameAndSerializedSessionDataItemsToStorePair, string? caseName);

    protected abstract IEnumerable<byte[]> RetrieveSerialized(string? caseName);
}