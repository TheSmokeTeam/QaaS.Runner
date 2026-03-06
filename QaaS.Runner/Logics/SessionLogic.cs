using Microsoft.Extensions.Logging;
using MoreLinq;
using QaaS.Framework.Executions.Logics;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Sessions.Session;

namespace QaaS.Runner.Logics;

/// <summary>
/// Coordinates session execution during runtime, including stage ordering and stage-level blocking.
/// </summary>
public class SessionLogic(List<ISession> sessions, InternalContext context) : ILogic
{
    /// <summary>
    /// Determines whether session execution should run for the requested execution type.
    /// </summary>
    /// <param name="executionType">The active execution pipeline mode.</param>
    /// <returns>
    /// <see langword="true" /> for <see cref="ExecutionType.Act" /> and <see cref="ExecutionType.Run" />;
    /// otherwise <see langword="false" />.
    /// </returns>
    public bool ShouldRun(ExecutionType executionType)
    {
        return executionType is ExecutionType.Act or ExecutionType.Run;
    }


    /// <summary>
    /// Runs all configured sessions in stage order and applies deferred blocking rules defined by
    /// <see cref="ISession.RunUntilStage" />.
    /// </summary>
    /// <param name="executionData">The mutable execution context that is populated with produced session data.</param>
    /// <returns>The same <paramref name="executionData" /> instance after session execution completes.</returns>
    public ExecutionData Run(ExecutionData executionData)
    {
        context.Logger.LogInformation("Running {LogicType} Logic", "Sessions");
        context.Logger.LogInformation("Received {SessionCount} session definitions for execution", sessions.Count);

        var stages = new SortedDictionary<int, List<ISession>>();
        foreach (var session in sessions)
        {
            if (!stages.ContainsKey(session.SessionStage))
                stages[session.SessionStage] = [];
            stages[session.SessionStage].Add(session);
        }
        context.Logger.LogDebug("Grouped sessions into {StageCount} stage buckets", stages.Count);

        var blockingSessions = new Dictionary<int, List<Task<SessionData?>>>();
        foreach (var (stage, stageSessions) in stages)
        {
            // initial list of sessions that run in current stage
            var sessionsInThisStage = new List<Task<SessionData?>>();

            if (blockingSessions.TryGetValue(stage, out var blockers))
            {
                context.Logger.LogDebug(
                    "Waiting for {BlockingSessionCount} deferred session(s) before starting stage {Stage}",
                    blockers.Count, stage);
                Task.WhenAll(blockers).Wait();
            }

            context.Logger.LogInformation(
                "Starting session stage {Stage} with {SessionCount} session(s): {SessionNames}",
                stage, stageSessions.Count, string.Join(", ", stageSessions.Select(session => session.Name)));
            foreach (var session in stageSessions)
            {
                if (session.RunUntilStage == null)
                {
                    sessionsInThisStage.Add(Task.Run(() => session.Run(executionData)));
                    continue;
                }

                if (!blockingSessions.ContainsKey(session.RunUntilStage!.Value))
                    blockingSessions[session.RunUntilStage.Value] = [];
                blockingSessions[session.RunUntilStage.Value].Add(Task.Run(() => session.Run(executionData)));
            }

            executionData.SessionDatas.AddRange(sessionsInThisStage.Select(s => s.Result));
            context.Logger.LogInformation(
                "Finished session stage {Stage}. Immediate session results captured: {CapturedSessionCount}",
                stage, sessionsInThisStage.Count);
        }

        blockingSessions.Select(stageToSessions => stageToSessions.Value)
            .ForEach(sessionsTasks => sessionsTasks
                .ForEach(sessionTask => executionData.SessionDatas.Add(sessionTask.Result)));

        context.Logger.LogInformation("Session logic completed. Total collected session results: {SessionDataCount}",
            executionData.SessionDatas.Count);

        return executionData;
    }
}
