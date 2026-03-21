using Microsoft.Extensions.Logging;
using MoreLinq;
using QaaS.Framework.Executions.Logics;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Sessions.Session;

namespace QaaS.Runner.Logics;

/// <summary>
/// Coordinates runtime session execution, including stage ordering, deferred completion, and
/// stage-to-stage visibility of produced <see cref="SessionData" /> instances.
/// </summary>
/// <remarks>
/// Session execution is driven by two values from <see cref="ISession" />:
/// <see cref="ISession.SessionStage" /> decides when a session starts, while
/// <see cref="ISession.RunUntilStage" /> optionally decides when the runner must wait for that
/// session to finish and publish its result into <see cref="ExecutionData.SessionDatas" />.
/// This allows a session to start in one stage, overlap one or more intermediate stages, and then
/// block a later stage where its data becomes required.
/// </remarks>
public class SessionLogic : ILogic
{
    private readonly IReadOnlyList<ISession> _sessions;
    private readonly InternalContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionLogic" /> class.
    /// </summary>
    /// <param name="sessions">
    /// The sessions selected for the current execution. The incoming order does not matter because
    /// <see cref="Run" /> normalizes them into sorted stage buckets.
    /// </param>
    /// <param name="context">
    /// The execution context that provides logging and shared state visible to session
    /// implementations.
    /// </param>
    /// <remarks>
    /// The <paramref name="sessions" /> collection is treated as immutable execution input. The
    /// logic mutates only the supplied <see cref="ExecutionData" /> instance.
    /// </remarks>
    public SessionLogic(List<ISession> sessions, InternalContext context)
    {
        _sessions = sessions;
        _context = context;
    }

    /// <summary>
    /// Runs all configured sessions in stage order and materializes their outputs into the supplied
    /// <see cref="ExecutionData" /> instance.
    /// </summary>
    /// <param name="executionData">
    /// The mutable execution context that receives each completed <see cref="SessionData" /> once
    /// its stage-level availability rules have been satisfied.
    /// </param>
    /// <returns>The same <paramref name="executionData" /> instance after session execution completes.</returns>
    /// <remarks>
    /// The algorithm has four phases:
    /// 1. Group sessions by <see cref="ISession.SessionStage" /> using a
    /// <see cref="SortedDictionary{TKey,TValue}" /> so stage order is deterministic even when the
    /// input list is unsorted.
    /// 2. Before each stage starts, wait for any deferred sessions whose
    /// <see cref="ISession.RunUntilStage" /> matches that stage and publish their results into
    /// <see cref="ExecutionData.SessionDatas" />.
    /// 3. Start every session assigned to the current stage. Sessions without
    /// <see cref="ISession.RunUntilStage" /> are materialized at the end of their own stage.
    /// Deferred sessions keep running in the background and are only materialized when their target
    /// stage is reached.
    /// 4. After the final configured stage, drain any remaining deferred sessions whose target stage
    /// never appeared. This preserves produced data instead of dropping it on the floor.
    ///
    /// Results are appended in task-list order, not completion order. That keeps the output stable
    /// for callers and tests even when individual session durations differ.
    ///
    /// <see cref="ISession.RunUntilStage" /> is expected to point to a future stage. If it points
    /// to a stage that never appears, the session is finalized during the end-of-run drain.
    /// </remarks>
    public ExecutionData Run(ExecutionData executionData)
    {
        _context.Logger.LogInformation("Running {LogicType} Logic", "Sessions");
        _context.Logger.LogInformation("Received {SessionCount} session definitions for execution", _sessions.Count);

        var stages = BuildStageMap();
        _context.Logger.LogDebug("Grouped sessions into {StageCount} stage buckets", stages.Count);

        var blockingSessionsByTargetStage = new Dictionary<int, List<Task<SessionData?>>>();
        foreach (var (stage, stageSessions) in stages)
        {
            MaterializeDeferredSessionsForStage(stage, blockingSessionsByTargetStage, executionData);

            _context.Logger.LogInformation(
                "Starting session stage {Stage} with {SessionCount} session(s): {SessionNames}",
                stage, stageSessions.Count, string.Join(", ", stageSessions.Select(session => session.Name)));
            var immediateSessionsInThisStage =
                StartSessionsForCurrentStage(stageSessions, executionData, blockingSessionsByTargetStage);

            // Sessions without RunUntilStage are visible to the next stage only after the full
            // current stage has been launched and completed.
            executionData.SessionDatas.AddRange(MaterializeSessionResults(immediateSessionsInThisStage));
            _context.Logger.LogDebug(
                "Finished session stage {Stage}. Immediate session results captured: {CapturedSessionCount}",
                stage, immediateSessionsInThisStage.Count);
        }

        MaterializeRemainingDeferredSessions(blockingSessionsByTargetStage, executionData);

        _context.Logger.LogInformation("Session logic completed. Total collected session results: {SessionDataCount}",
            executionData.SessionDatas.Count);

        return executionData;
    }

    /// <summary>
    /// Builds a stage-ordered view of the configured sessions.
    /// </summary>
    /// <remarks>
    /// <see cref="SortedDictionary{TKey,TValue}" /> is used intentionally so stage execution order
    /// is derived from the numeric stage value rather than from whichever order the caller supplied.
    /// </remarks>
    private SortedDictionary<int, List<ISession>> BuildStageMap()
    {
        var stages = new SortedDictionary<int, List<ISession>>();
        foreach (var session in _sessions)
        {
            if (!stages.ContainsKey(session.SessionStage))
                stages[session.SessionStage] = [];

            stages[session.SessionStage].Add(session);
        }

        return stages;
    }

    /// <summary>
    /// Waits for deferred sessions that must complete before the specified stage can begin.
    /// </summary>
    /// <param name="stage">The stage that is about to start.</param>
    /// <param name="blockingSessionsByTargetStage">
    /// Deferred sessions keyed by the stage that should wait for them.
    /// </param>
    /// <param name="executionData">The execution data that receives the finalized session results.</param>
    private void MaterializeDeferredSessionsForStage(int stage,
        IDictionary<int, List<Task<SessionData?>>> blockingSessionsByTargetStage, ExecutionData executionData)
    {
        if (!blockingSessionsByTargetStage.Remove(stage, out var blockers))
            return;

        _context.Logger.LogDebug(
            "Waiting for {BlockingSessionCount} deferred session(s) before starting stage {Stage}",
            blockers.Count, stage);
        Task.WhenAll(blockers).GetAwaiter().GetResult();
        executionData.SessionDatas.AddRange(MaterializeSessionResults(blockers));
        _context.Logger.LogDebug(
            "Materialized {BlockingSessionCount} deferred session result(s) before stage {Stage}",
            blockers.Count, stage);
    }

    /// <summary>
    /// Starts every session assigned to the current stage and separates immediate sessions from
    /// deferred ones.
    /// </summary>
    /// <param name="stageSessions">The sessions that start in the current stage.</param>
    /// <param name="executionData">The execution data passed to each session.</param>
    /// <param name="blockingSessionsByTargetStage">
    /// Deferred sessions keyed by the future stage that must wait for them.
    /// </param>
    /// <returns>
    /// Tasks for sessions whose results should be materialized at the end of the current stage.
    /// </returns>
    /// <remarks>
    /// A deferred session still starts immediately in its own <see cref="ISession.SessionStage" />.
    /// Only publication of its result is postponed.
    /// </remarks>
    private List<Task<SessionData?>> StartSessionsForCurrentStage(IEnumerable<ISession> stageSessions,
        ExecutionData executionData, IDictionary<int, List<Task<SessionData?>>> blockingSessionsByTargetStage)
    {
        var immediateSessionsInThisStage = new List<Task<SessionData?>>();

        foreach (var session in stageSessions)
        {
            var sessionTask = StartSessionAsync(session, executionData);
            if (session.RunUntilStage is not int targetStage)
            {
                immediateSessionsInThisStage.Add(sessionTask);
                continue;
            }

            if (!blockingSessionsByTargetStage.ContainsKey(targetStage))
                blockingSessionsByTargetStage[targetStage] = [];

            blockingSessionsByTargetStage[targetStage].Add(sessionTask);
            _context.Logger.LogDebug(
                "Deferred session {SessionName} started in stage {SessionStage} and will block stage {TargetStage}",
                session.Name, session.SessionStage, targetStage);
        }

        return immediateSessionsInThisStage;
    }

    /// <summary>
    /// Finalizes deferred sessions that never matched an encountered stage.
    /// </summary>
    /// <param name="blockingSessionsByTargetStage">
    /// Deferred sessions still pending after the last configured stage.
    /// </param>
    /// <param name="executionData">The execution data that receives the finalized session results.</param>
    /// <remarks>
    /// This is a best-effort cleanup path. It avoids silently losing results when a deferred session
    /// points at a stage that is not present in the execution plan.
    /// </remarks>
    private static void MaterializeRemainingDeferredSessions(
        IDictionary<int, List<Task<SessionData?>>> blockingSessionsByTargetStage, ExecutionData executionData)
    {
        blockingSessionsByTargetStage.Select(stageToSessions => stageToSessions.Value)
            .ForEach(sessionTasks => executionData.SessionDatas.AddRange(MaterializeSessionResults(sessionTasks)));
    }

    /// <summary>
    /// Materializes completed session tasks into their produced session data in deterministic task
    /// order.
    /// </summary>
    /// <param name="sessionTasks">The completed or awaitable session tasks to materialize.</param>
    /// <returns>The produced session results, preserving task enumeration order.</returns>
    private static IEnumerable<SessionData?> MaterializeSessionResults(IEnumerable<Task<SessionData?>> sessionTasks)
    {
        return sessionTasks.Select(sessionTask => sessionTask.GetAwaiter().GetResult());
    }

    /// <summary>
    /// Starts a session through its asynchronous entry point.
    /// </summary>
    /// <param name="session">The session to execute.</param>
    /// <param name="executionData">The shared execution data visible to the session.</param>
    /// <returns>The task representing the session execution.</returns>
    /// <remarks>
    /// The logic always goes through <see cref="ISession.RunAsync" /> so synchronous-only sessions
    /// can rely on the interface's default bridge while truly asynchronous implementations can avoid
    /// blocking worker threads.
    /// </remarks>
    private static Task<SessionData?> StartSessionAsync(ISession session, ExecutionData executionData)
    {
        return session.RunAsync(executionData);
    }
}
