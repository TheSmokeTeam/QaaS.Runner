using Microsoft.Extensions.Logging;
using QaaS.Framework.Policies;
using QaaS.Framework.SDK.ConfigurationObjects;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session.DataObjects;

namespace QaaS.Runner.Sessions.Actions;

public abstract class StagedAction : Action
{
    protected readonly Policy? Policies;

    public int Stage { get; internal set; }

    public StagedAction(string name, int stage, Policy? policies, ILogger logger) : base(name, logger)
    {
        Name = name;
        Stage = stage;
        Policies = policies;
    }

    /// <summary>
    /// Exports the rcd to the InternalContext of the system so that other parts of qaas will be able to use
    /// the live data.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="sessionName"> The running session name. It is used to save the rcd under the session in the context. </param>
    internal abstract void ExportRunningCommunicationData(InternalContext context, string sessionName);

    /// <summary>
    /// Logs given data to a rcd and CommunicationData object.
    /// Is used under the publisher action for example, for logging data after it was sent.
    /// </summary>
    /// <param name="actData"> Is used as the return value of a communicaiton object that holds all the data sent, consumed, etc... </param>
    /// <param name="itemBeforeSerialization"> The raw item that was pub/subbed before it was serialized </param>
    /// <param name="saveAt">Save at input, output or none of the data store lists </param>
    protected internal abstract void LogData(InternalCommunicationData<object> actData,
        DetailedData<object> itemBeforeSerialization, InputOutputState? saveAt = null);
}