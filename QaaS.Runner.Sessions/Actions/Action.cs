using Microsoft.Extensions.Logging;

namespace QaaS.Runner.Sessions.Actions;

public abstract class Action
{
    protected readonly ILogger Logger;

    public Action(string name, ILogger logger)
    {
        Name = name;
        Logger = logger;
        Logger.LogDebug("Initialized action {ActionType} {ActionName}", GetType().Name, Name);
    }

    public string Name { get; init; }
    internal abstract InternalCommunicationData<object> Act();
}
