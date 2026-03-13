using Microsoft.Extensions.Logging;

namespace QaaS.Runner.Sessions.Actions;

public abstract class Action : IDisposable
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

    /// <summary>
    /// Releases action-owned resources after the session has finished using the action result.
    /// </summary>
    public virtual void Dispose()
    {
    }
}
