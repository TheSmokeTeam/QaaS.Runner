using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Storage.ConfigurationObjects;

namespace QaaS.Runner.Storage.Tests;

[TestFixture]
public class BaseStorageTests
{
    private sealed class InspectableStorage(Formatting formatting, IEnumerable<byte[]>? retrieved = null)
        : BaseStorage(formatting)
    {
        public IList<KeyValuePair<string, byte[]>> StoredItems { get; private set; } = [];
        public string? CaseName { get; private set; }

        protected override void StoreSerialized(
            IList<KeyValuePair<string, byte[]>> sessionFileNameAndSerializedSessionDataItemsToStorePair, string? caseName)
        {
            StoredItems = sessionFileNameAndSerializedSessionDataItemsToStorePair;
            CaseName = caseName;
        }

        protected override IEnumerable<byte[]> RetrieveSerialized(string? caseName)
        {
            CaseName = caseName;
            return retrieved ?? [];
        }
    }

    [Test]
    public void Store_WithNullSessionDataList_StoresEmptyCollection()
    {
        var storage = new InspectableStorage(Formatting.None);

        storage.Store(null, "case-a");

        Assert.That(storage.StoredItems, Is.Empty);
        Assert.That(storage.CaseName, Is.EqualTo("case-a"));
    }

    [Test]
    public void Store_WithNullSessionEntries_StoresOnlyNonNullSessions()
    {
        var storage = new InspectableStorage(Formatting.None);
        var session = new SessionData { Name = "session-a" };
        var sessions = new List<SessionData?> { session, null }.ToImmutableList();

        storage.Store(sessions, "case-b");

        Assert.That(storage.StoredItems, Has.Count.EqualTo(1));
        Assert.That(storage.StoredItems.Single().Key, Is.EqualTo("session-a.json"));
        Assert.That(storage.StoredItems.Single().Value, Is.Not.Empty);
    }

    [Test]
    public void Retrieve_DeserializesAllRetrievedPayloads()
    {
        var firstSession = new SessionData { Name = "session-a" };
        var secondSession = new SessionData { Name = "session-b" };
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var storage = new InspectableStorage(Formatting.None,
        [
            SessionDataSerialization.SerializeSessionData(firstSession, options),
            SessionDataSerialization.SerializeSessionData(secondSession, options)
        ]);

        var result = storage.Retrieve("case-c");

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Select(session => session.Name), Is.EquivalentTo(["session-a", "session-b"]));
        Assert.That(storage.CaseName, Is.EqualTo("case-c"));
    }

    [Test]
    public void Store_WithMissingSessionName_ThrowsInvalidOperationException()
    {
        var storage = new InspectableStorage(Formatting.None);
        var sessions = new List<SessionData?> { new() { Name = "" } }.ToImmutableList();

        Assert.Throws<InvalidOperationException>(() => storage.Store(sessions, "case-d"));
    }

    [Test]
    public void Store_WithDuplicateNormalizedFileNames_ThrowsInvalidOperationException()
    {
        var storage = new InspectableStorage(Formatting.None);
        var sessions = new List<SessionData?>
        {
            new() { Name = "session/a" },
            new() { Name = "session\\a" }
        }.ToImmutableList();

        Assert.Throws<InvalidOperationException>(() => storage.Store(sessions, "case-e"));
    }
}
