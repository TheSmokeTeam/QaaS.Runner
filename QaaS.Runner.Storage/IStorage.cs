using System.Collections.Immutable;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.Storage;

public interface IStorage
{
    public void Store(ImmutableList<SessionData?>? sessionDataList, string? caseName);

    public ImmutableList<SessionData> Retrieve(string? caseName);
}