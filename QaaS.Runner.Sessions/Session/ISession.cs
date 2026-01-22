using System.Collections.Concurrent;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.Sessions.Session;

public interface ISession
{
    public string Name { get; }

    public int? RunUntilStage { get; }

    public int SessionStage { get; }
    
    public SessionData? Run(ExecutionData executionData);


}