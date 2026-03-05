using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MoreLinq.Extensions;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Session.CommunicationDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Sessions.Actions;
using QaaS.Runner.Sessions.Actions.Collectors;
using QaaS.Runner.Sessions.Extensions;
using Action = QaaS.Runner.Sessions.Actions.Action;

namespace QaaS.Runner.Sessions.Session;

public class Session : ISession
{
    private readonly ConcurrentBag<ActionFailure> _actionFailures;
    private readonly Collector[]? _collectors;
    private readonly InternalContext _context;

    private readonly Dictionary<int, Stage> _stages;

    public int? RunUntilStage { get; }

    internal Session(
        string name,
        int sessionStage,
        bool saveData,
        uint timeoutBeforeSessionMs,
        uint timeoutAfterSessionMs,
        Dictionary<int, Stage> stages,
        Collector[]? collectors,
        InternalContext context,
        ConcurrentBag<ActionFailure> actionFailures,
        int? runUntil = null)
    {
        Name = name;
        _actionFailures = actionFailures;
        SessionStage = sessionStage;
        SaveData = saveData;
        TimeoutBeforeSessionMs = timeoutBeforeSessionMs;
        TimeoutAfterSessionMs = timeoutAfterSessionMs;
        _stages = stages;
        _collectors = collectors;
        RunUntilStage = runUntil;
        _context = context;
    }
    
    public string Name { get; }
    private bool SaveData { get; }
    public int SessionStage { get; }
    private uint TimeoutBeforeSessionMs { get; }
    private uint TimeoutAfterSessionMs { get; }
    
    /// <summary>
    /// Executes all stages in the session, one after another by order.
    /// </summary>
    protected virtual DateTime GetCurrentUtcTime()
    {
        return DateTime.UtcNow;
    }

    /// <summary>
    /// Executes all stages in the session, one after another by order.
    /// </summary>
    /// <param name="executionData"> Contains Datasource data. Will eventually contain the session data </param>
    /// <returns> If SaveData is set to true then it will return the session data </returns>
    public SessionData? Run(ExecutionData executionData)
    {
        _context.Logger.LogDebug("Waiting the configured timeout of {TimeoutBeforeSessionMs}" +
                                 " milliseconds before the start of the session {SessionName}",
            TimeoutBeforeSessionMs, Name);
        Thread.Sleep(TimeSpan.FromMilliseconds(TimeoutBeforeSessionMs));

        _context.Logger.LogInformation("Started running session - {SessionName}", Name);
        var sessionStartTimeUtc = GetCurrentUtcTime();
        var actionsTasks = new List<Task<Tuple<Action, InternalCommunicationData<object>>?>>();

        InitializeSessionRun(executionData);
        foreach (var (_, stage) in _stages.OrderBy(stage => stage.Key))
            actionsTasks.AddRange(stage.Run());

        Task.WhenAll(actionsTasks).Wait();
        actionsTasks.DisposeOfEnumerable("intermediate session tasks", _context.Logger);
        var sessionEndTimeUtc = GetCurrentUtcTime();

        // Perform end session operations
        var postSessionsTasks =
            RunPostSessionTasksAsync(sessionStartTimeUtc, sessionEndTimeUtc).GetAwaiter().GetResult();
        actionsTasks.AddRange(postSessionsTasks);

        var sessionData = CreateSessionData(actionsTasks, sessionStartTimeUtc, sessionEndTimeUtc);

        // Removing current session from running sessions
        _context.InternalRunningSessions.RunningSessionsDict.Remove(Name);

        LogSessionSummary(sessionData);

        // If session is configured to not save session data
        if (!SaveData)
            _context.Logger.LogInformation("Not saving session output of session {SessionName}", Name);

        _context.Logger.LogDebug("Waiting the configured timeout of {TimeoutAfterSessionMs} " +
                                 "milliseconds after the end of the session {SessionName}",
            TimeoutAfterSessionMs, Name);
        Thread.Sleep(TimeSpan.FromMilliseconds(TimeoutAfterSessionMs));

        return SaveData ? sessionData : null;
    }

    /// <summary>
    ///     initializing the running of the session by exporting the running session data and populating data for publishing
    ///     actions
    /// </summary>
    private void InitializeSessionRun(ExecutionData executionData)
    {
        _context.InternalRunningSessions.RunningSessionsDict[Name] =
            new RunningSessionData<object, object> { Inputs = [], Outputs = [] };
        foreach (var stage in _stages.Values)
        {
            stage.ExportRunningCommunicationData();
            stage.PrepareActions(executionData.SessionDatas, executionData.DataSources);
        }
    }

    private async Task<List<Task<Tuple<Action, InternalCommunicationData<object>>?>>> RunPostSessionTasksAsync(
        DateTime sessionStartTimeUtc, DateTime sessionEndTimeUtc)
    {
        _collectors?.ForEach(collector => collector.SetCollectionTimes(sessionStartTimeUtc, sessionEndTimeUtc));
        var collectorTasks = _collectors?.Select(collector =>
            SessionExtensions.CreateTaskFromAction(_context, collector, Name, _actionFailures)).ToList() ?? [];
        foreach (var task in collectorTasks)
            task.Start();

        await Task.WhenAll(collectorTasks);

        collectorTasks.DisposeOfEnumerable("post session tasks", _context.Logger);
        return collectorTasks;
    }


    /// <summary>
    ///     Creating session data object from the actions' results
    /// </summary>
    /// <param name="actionsTasks"></param>
    /// <param name="sessionStartTime"></param>
    /// <param name="sessionEndTime"></param>
    /// <returns></returns>
    private SessionData CreateSessionData(List<Task<Tuple<Action, InternalCommunicationData<object>>?>> actionsTasks,
        DateTime sessionStartTime, DateTime sessionEndTime)
    {
        var sessionData = new SessionData
        {
            Inputs = [],
            Outputs = [],
            Name = Name,
            SessionFailures = _actionFailures.ToList(),
            UtcStartTime = sessionStartTime,
            UtcEndTime = sessionEndTime
        };

        foreach (var actionTask in actionsTasks)
        {
            var internalCommunicationData = actionTask.Result?.Item2;
            var action = actionTask.Result?.Item1;
            if (internalCommunicationData?.Input != null)
            {
                var serializationType = internalCommunicationData.InputSerializationType;
                sessionData.Inputs.Add(new CommunicationData<object>
                {
                    Data = internalCommunicationData.Input, Name = action!.Name, SerializationType = serializationType
                });
            }

            if (internalCommunicationData?.Output != null)
            {
                var serializationType = internalCommunicationData.OutputSerializationType;
                sessionData.Outputs.Add(new CommunicationData<object>
                {
                    Data = internalCommunicationData.Output.Where(output => output != null).ToList()!,
                    Name = action!.Name, SerializationType = serializationType
                });
            }
        }

        return sessionData;
    }

    private void LogSessionSummary(SessionData sessionData)
    {
        _context.Logger.LogInformation("--- Session {SessionName} Summary ---", sessionData.Name);
        _context.Logger.LogInformationWithMetaData(
            "{SessionName} Duration In Milliseconds: {SessionDurationMilliseconds}",
            _context.GetMetaDataFromContext(),
            new object?[] { sessionData.Name, (sessionData.UtcEndTime - sessionData.UtcStartTime).TotalMilliseconds });
        _context.Logger.LogInformation("Session Utc Start Time: {SessionUtcStartTime}",
            sessionData.UtcStartTime);
        _context.Logger.LogInformation("Session Utc End Time: {SessionUtcEndTime}", sessionData.UtcEndTime);

        // Inputs summary
        foreach (var input in sessionData.Inputs ?? Enumerable.Empty<CommunicationData<object>>())
        {
            var numberOfInputs = input.Data.Count;
            _context.Logger.LogInformation(
                "Input Source {InputName} Contains {NumberOfInputsSentToSource} Inputs",
                input.Name, numberOfInputs);
        }

        // Outputs summary
        foreach (var output in sessionData.Outputs ?? Enumerable.Empty<CommunicationData<object>>())
        {
            var numberOfOutputs = output.Data.Count;
            _context.Logger.LogInformation(
                "Output Source {OutputName} Contains {NumberOfOutputsSentToSource} Outputs",
                output.Name, numberOfOutputs);
        }

        _context.Logger.LogInformation("--- End Of Summary ---");
    }
}
