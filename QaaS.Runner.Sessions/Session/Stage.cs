using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
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
        _context.Logger.LogInformation("Sleeping {WaitTimeMs} milliseconds before stage starts",
            SleepBeforeMilliseconds);
        if (SleepBeforeMilliseconds != null)
            Thread.Sleep((int)SleepBeforeMilliseconds);
        _context.Logger.LogInformation(
            "Starting Session {SessionName}'s stage number {StageNumber} - containing {NumberOfActions} actions",
            _sessionName, _stage, Actions.Count);

        var stageTasks =
            Actions.Select(action =>
                SessionExtensions.CreateTaskFromAction(_context, action, _sessionName, _actionFailures)).ToList();
        stageTasks.ForEach(task => task.Start());

        _context.Logger.LogInformation(
            "Finished Session {SessionName}'s stage number {StageNumber}. Sleeping {WaitTimeMs} milliseconds",
            _sessionName, _stage, SleepAfterMilliseconds);

        if (SleepAfterMilliseconds != null)
            Thread.Sleep((int)SleepAfterMilliseconds);

        return stageTasks;
    }
}