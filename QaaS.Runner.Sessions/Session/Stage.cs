using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Infrastructure;
using QaaS.Runner.Sessions.Actions;
using QaaS.Runner.Sessions.Actions.Probes;
using QaaS.Runner.Sessions.Actions.Publishers;
using QaaS.Runner.Sessions.Actions.Transactions;
using QaaS.Runner.Sessions.Extensions;
using Action = QaaS.Runner.Sessions.Actions.Action;


namespace QaaS.Runner.Sessions.Session;

public class Stage
{
    private readonly ConcurrentBag<ActionFailure> _actionFailures;
    private readonly InternalContext _context;
    private readonly string _sessionName;
    private readonly int _stage;

    public Stage(InternalContext context, ConcurrentBag<ActionFailure> actionFailures, string sessionName, int stage,
        int? sleepBeforeMilliseconds = 0, int? sleepAfterMilliseconds = 2000)
    {
        _stage = stage;
        _context = context;
        _actionFailures = actionFailures;
        SleepBeforeMilliseconds = sleepBeforeMilliseconds;
        SleepAfterMilliseconds = sleepAfterMilliseconds;
        _sessionName = sessionName;
    }

    private List<StagedAction> Actions { get; set; } = [];
    private int? SleepBeforeMilliseconds { get; }
    private int? SleepAfterMilliseconds { get; }

    public void AddCommunication(StagedAction stagedAction)
    {
        Actions.Add(stagedAction);
    }


    public void ExportRunningCommunicationData()
    {
        foreach (var communication in Actions)
            communication.ExportRunningCommunicationData(_context, _sessionName);
    }

    public void PrepareActions(List<SessionData?> ranSessions, List<DataSource> dataSources)
    {
        foreach (var communication in Actions)
        {
            switch (communication)
            {
                case Publisher publisher:
                    publisher.InitializeIterableSerializableSaveIterator(ranSessions, dataSources);
                    break;
                case ChunkPublisher publisher:
                    publisher.InitializeIterableSerializableSaveIterator(ranSessions, dataSources);
                    break;
                case Transaction transaction:
                    transaction.InitializeIterableSerializableSaveIterator(ranSessions, dataSources);
                    break;
                case Probe probe:
                    probe.InitializeIterableSerializableSaveIterator(ranSessions, dataSources);
                    break;
            }
        }
    }

    public IList<Task<Tuple<Action, InternalCommunicationData<object>>?>> Run()
    {
        if (SleepBeforeMilliseconds is > 0)
        {
            _context.Logger.LogDebug("Sleeping {WaitTimeMs} ms before session {SessionName} stage {StageNumber}",
                SleepBeforeMilliseconds, _sessionName, _stage);
            Thread.Sleep((int)SleepBeforeMilliseconds);
        }
        _context.Logger.LogInformation(
            "Starting session {SessionName} stage {StageNumber} with {ActionCount} action(s)",
            _sessionName, _stage, Actions.Count);
        _context.AppendSessionLog(_sessionName,
            $"Starting session {_sessionName} stage {_stage} with {Actions.Count} action(s)");
        _context.Logger.LogDebug("Session {SessionName} stage {StageNumber} actions: {ActionNames}",
            _sessionName, _stage, string.Join(", ", Actions.Select(action => $"{action.GetType().Name}:{action.Name}")));

        var stageTasks =
            Actions.Select(action =>
                SessionExtensions.CreateTaskFromAction(_context, action, _sessionName, _actionFailures)).ToList();
        stageTasks.ForEach(task => task.Start());
        _ = Task.WhenAll(stageTasks).ContinueWith(_ =>
            {
                _context.Logger.LogInformation("Finished session {SessionName} stage {StageNumber}",
                    _sessionName, _stage);
                _context.AppendSessionLog(_sessionName, $"Finished session {_sessionName} stage {_stage}");
            },
            TaskScheduler.Default);

        if (SleepAfterMilliseconds is > 0)
        {
            _context.Logger.LogDebug("Sleeping {WaitTimeMs} ms after session {SessionName} stage {StageNumber}",
                SleepAfterMilliseconds, _sessionName, _stage);
            Thread.Sleep((int)SleepAfterMilliseconds);
        }

        return stageTasks;
    }
}
