using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MoreLinq.Extensions;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Session.CommunicationDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Infrastructure;
using QaaS.Runner.Sessions.Actions;
using QaaS.Runner.Sessions.Actions.Collectors;
using QaaS.Runner.Sessions.Extensions;
using Action = QaaS.Runner.Sessions.Actions.Action;

namespace QaaS.Runner.Sessions.Session;

/// <summary>
/// Executes the configured stages and collectors for a single runtime session.
/// </summary>
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
        return RunAsync(executionData).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async Task<SessionData?> RunAsync(ExecutionData executionData)
    {
        _context.Logger.LogDebug("Waiting {TimeoutBeforeSessionMs} ms before starting session {SessionName}",
            TimeoutBeforeSessionMs, Name);
        await Task.Delay(TimeSpan.FromMilliseconds(TimeoutBeforeSessionMs));

        _context.Logger.LogInformation("Starting session {SessionName}", Name);
        _context.AppendSessionLog(Name, $"Starting session {Name}");
        var sessionStartTimeUtc = GetCurrentUtcTime();
        var actionsTasks = new List<Task<Tuple<Action, InternalCommunicationData<object>>?>>();

        InitializeSessionRun(executionData);
        _context.Logger.LogDebug("Session {SessionName} contains {StageCount} stage(s) and {CollectorCount} collector(s)",
            Name, _stages.Count, _collectors?.Length ?? 0);
        foreach (var (_, stage) in _stages.OrderBy(stage => stage.Key))
            actionsTasks.AddRange(await stage.RunAsync());

        await Task.WhenAll(actionsTasks);
        var sessionEndTimeUtc = GetCurrentUtcTime();

        // Perform end session operations
        var postSessionsTasks = await RunPostSessionTasksAsync(sessionStartTimeUtc, sessionEndTimeUtc);
        actionsTasks.AddRange(postSessionsTasks);

        var sessionData = CreateSessionData(actionsTasks, sessionStartTimeUtc, sessionEndTimeUtc);
        actionsTasks.DisposeOfEnumerable("session tasks", _context.Logger);
        _stages.Values.SelectMany(stage => stage.GetActions())
            .Concat<Action>(_collectors ?? [])
            .DisposeOfEnumerable("session actions", _context.Logger);

        // Removing current session from running sessions
        _context.RemoveRunningSession(Name);

        LogSessionSummary(sessionData);

        // If session is configured to not save session data
        if (!SaveData)
            _context.Logger.LogInformation("Session {SessionName} is configured not to persist output data", Name);

        _context.Logger.LogDebug("Waiting {TimeoutAfterSessionMs} ms after finishing session {SessionName}",
            TimeoutAfterSessionMs, Name);
        await Task.Delay(TimeSpan.FromMilliseconds(TimeoutAfterSessionMs));

        return SaveData ? sessionData : null;
    }

    /// <summary>
    ///     initializing the running of the session by exporting the running session data and populating data for publishing
    ///     actions
    /// </summary>
    private void InitializeSessionRun(ExecutionData executionData)
    {
        _context.SetRunningSession(Name, new RunningSessionData<object, object> { Inputs = [], Outputs = [] });
        _context.Logger.LogDebug("Preparing session {SessionName} with {ExistingSessionCount} existing session result(s) and {DataSourceCount} data source(s)",
            Name, executionData.SessionDatas.Count, executionData.DataSources.Count);
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
        _context.Logger.LogDebug("Running {CollectorCount} collector task(s) after session {SessionName}",
            collectorTasks.Count, Name);
        await Task.WhenAll(collectorTasks);
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

        _context.Logger.LogDebug(
            "Built session data for {SessionName}. Inputs={InputCount}, Outputs={OutputCount}, Failures={FailureCount}",
            Name, sessionData.Inputs.Count, sessionData.Outputs.Count, sessionData.SessionFailures.Count);

        return sessionData;
    }

    private void LogSessionSummary(SessionData sessionData)
    {
        _context.Logger.LogInformation("Session summary for {SessionName}", sessionData.Name);
        _context.AppendSessionLog(sessionData.Name, $"Session summary for {sessionData.Name}");
        _context.Logger.LogInformationWithMetaData(
            "{SessionName} Duration In Milliseconds: {SessionDurationMilliseconds}",
            _context.GetMetaDataOrDefault(),
            new object?[] { sessionData.Name, (sessionData.UtcEndTime - sessionData.UtcStartTime).TotalMilliseconds });
        _context.AppendSessionLog(sessionData.Name,
            $"{sessionData.Name} Duration In Milliseconds: {(sessionData.UtcEndTime - sessionData.UtcStartTime).TotalMilliseconds}");
        _context.Logger.LogInformation("Session Utc Start Time: {SessionUtcStartTime}",
            sessionData.UtcStartTime);
        _context.AppendSessionLog(sessionData.Name, $"Session Utc Start Time: {sessionData.UtcStartTime}");
        _context.Logger.LogInformation("Session Utc End Time: {SessionUtcEndTime}", sessionData.UtcEndTime);
        _context.AppendSessionLog(sessionData.Name, $"Session Utc End Time: {sessionData.UtcEndTime}");

        // Inputs summary
        foreach (var input in sessionData.Inputs ?? Enumerable.Empty<CommunicationData<object>>())
        {
            var numberOfInputs = input.Data.Count;
            _context.Logger.LogInformation(
                "Input Source {InputName} Contains {NumberOfInputsSentToSource} Inputs",
                input.Name, numberOfInputs);
            _context.AppendSessionLog(sessionData.Name,
                $"Input Source {input.Name} Contains {numberOfInputs} Inputs");
        }

        // Outputs summary
        foreach (var output in sessionData.Outputs ?? Enumerable.Empty<CommunicationData<object>>())
        {
            var numberOfOutputs = output.Data.Count;
            _context.Logger.LogInformation(
                "Output Source {OutputName} Contains {NumberOfOutputsSentToSource} Outputs",
                output.Name, numberOfOutputs);
            _context.AppendSessionLog(sessionData.Name,
                $"Output Source {output.Name} Contains {numberOfOutputs} Outputs");
        }

        _context.Logger.LogInformation("Session {SessionName} completed.", sessionData.Name);
        _context.AppendSessionLog(sessionData.Name, $"Session {sessionData.Name} completed.");
        _context.Logger.LogInformation("Session {SessionName} Inputs={InputCount}", sessionData.Name,
            sessionData.Inputs?.Count ?? 0);
        _context.AppendSessionLog(sessionData.Name,
            $"Session {sessionData.Name} Inputs={sessionData.Inputs?.Count ?? 0}");
        _context.Logger.LogInformation("Session {SessionName} Outputs={OutputCount}", sessionData.Name,
            sessionData.Outputs?.Count ?? 0);
        _context.AppendSessionLog(sessionData.Name,
            $"Session {sessionData.Name} Outputs={sessionData.Outputs?.Count ?? 0}");
        _context.Logger.LogInformation("Session {SessionName} Failures={FailureCount}", sessionData.Name,
            sessionData.SessionFailures?.Count ?? 0);
        _context.AppendSessionLog(sessionData.Name,
            $"Session {sessionData.Name} Failures={sessionData.SessionFailures?.Count ?? 0}");
    }
}
