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
    /// Runs all configured sessions in stage order and applies deferred blocking rules defined by
    /// <see cref="ISession.RunUntilStage" />.
    /// </summary>
    /// <param name="executionData">The mutable execution context that is populated with produced session data.</param>
    /// <returns>The same <paramref name="executionData" /> instance after session execution completes.</returns>
    public ExecutionData Run(ExecutionData executionData)
    {
        context.Logger.LogInformation("Running {LogicType} Logic", "Sessions");
        context.Logger.LogInformation("{NumberOfSessions} sessions were given", sessions.Count);

        var stages = new SortedDictionary<int, List<ISession>>();
        foreach (var session in sessions)
        {
            if (!stages.ContainsKey(session.SessionStage))
                stages[session.SessionStage] = [];
            stages[session.SessionStage].Add(session);
        }

        var blockingSessions = new Dictionary<int, List<Task<SessionData?>>>();
        foreach (var (stage, stageSessions) in stages)
        {
            // initial list of sessions that run in current stage
            var sessionsInThisStage = new List<Task<SessionData?>>();

            if (blockingSessions.TryGetValue(stage, out var blockers))
                Task.WhenAll(blockers).Wait();

            context.Logger.LogInformation("Starting session stage number {Stage} containing {SessionsCount} sessions",
                stage, stageSessions.Count);
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
        }

        blockingSessions.Select(stageToSessions => stageToSessions.Value)
            .ForEach(sessionsTasks => sessionsTasks
                .ForEach(sessionTask => executionData.SessionDatas.Add(sessionTask.Result)));

        return executionData;
    }
}
