using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Runner.Sessions.Actions;
using Action = QaaS.Runner.Sessions.Actions.Action;

namespace QaaS.Runner.Sessions.Extensions;

public static class SessionExtensions
{
    /// <summary>
    ///     Disposes of an enumerable of items that extend the `IDisposable` interface
    /// </summary>
    public static void DisposeOfEnumerable<TEnumerable>(this IEnumerable<TEnumerable>? enumerable,
        string enumerableName, ILogger logger)
        where TEnumerable : IDisposable
    {
        var array = (enumerable ?? Enumerable.Empty<TEnumerable>()).ToArray();
        foreach (var item in array)
            item.Dispose();

        logger.LogDebug("Disposed {EnumerableLength} item(s) from {EnumerableName}", array.Length, enumerableName);
    }

    /// <summary>
    ///     Appends a failed action to the action failure list, and logs accordingly.
    /// </summary>
    /// <param name="actionFailures">Action Failures list</param>
    /// <param name="exception">The action exception</param>
    /// <param name="sessionName">Session name</param>
    /// <param name="logger">A logger</param>
    /// <param name="actionType">The action type (Publisher, Consumer etc...)</param>
    /// <param name="actionProtocol">The action protocol (RabbitMq, KafkaTopic etc...)</param>
    /// <param name="actionRuntimeName">The action runtime name (if exists)</param>
    public static void AppendActionFailure(this IList<ActionFailure> actionFailures, Exception exception,
        string sessionName, ILogger logger, string actionType, string actionRuntimeName, string? actionProtocol = null)
    {
        var failedActionDescription = string.IsNullOrWhiteSpace(actionProtocol)
            ? actionRuntimeName
            : $"{actionProtocol} {actionRuntimeName}";
        logger.LogError(
            exception,
            "Action failure in session {SessionName}. ActionType={ActionType}, Action={ActionName}",
            sessionName, actionType, failedActionDescription);

        actionFailures.Add(new ActionFailure
        {
            Name = actionRuntimeName,
            ActionType = actionType,
            Reason = new Reason
            {
                Message = exception.Message,
                Description = exception.ToString()
            }
        });
    }

    public static void AppendActionFailure(this ConcurrentBag<ActionFailure> actionFailures, Exception exception,
        string sessionName, ILogger logger, string actionType, string actionRuntimeName, string? actionProtocol = null,
        string? exceptionMessage = null)
    {
        var failedActionDescription = string.IsNullOrWhiteSpace(actionProtocol)
            ? actionRuntimeName
            : $"{actionProtocol} {actionRuntimeName}";
        logger.LogError(
            exception,
            "Action failure in session {SessionName}. ActionType={ActionType}, Action={ActionName}",
            sessionName, actionType, failedActionDescription);

        actionFailures.Add(new ActionFailure
        {
            Name = actionRuntimeName,
            ActionType = actionType,
            Reason = new Reason
            {
                Message = exceptionMessage ?? exception.Message,
                Description = exception.ToString()
            }
        });
    }

    public static void SetRunningSession(this InternalContext context, string sessionName,
        RunningSessionData<object, object> runningSessionData)
    {
        // Running session data is shared across async actions, so updates must stay serialized to
        // avoid partial state being observed by publishers, consumers, and transactions.
        lock (context.InternalRunningSessions.RunningSessionsDict)
        {
            context.InternalRunningSessions.RunningSessionsDict[sessionName] = runningSessionData;
        }

        context.Logger.LogDebug("Registered running session state for {SessionName}", sessionName);
    }

    public static RunningSessionData<object, object> GetRunningSession(this InternalContext context, string sessionName)
    {
        // Reads use the same lock as writes so action cancellation and live session lookups stay coherent.
        lock (context.InternalRunningSessions.RunningSessionsDict)
        {
            return context.InternalRunningSessions.RunningSessionsDict[sessionName];
        }
    }

    public static bool RemoveRunningSession(this InternalContext context, string sessionName)
    {
        var removed = false;
        // Removing under the same lock prevents teardown from racing with actions that still resolve session state.
        lock (context.InternalRunningSessions.RunningSessionsDict)
        {
            removed = context.InternalRunningSessions.RunningSessionsDict.Remove(sessionName);
        }

        context.Logger.LogDebug("Removed running session state for {SessionName}: {Removed}", sessionName, removed);
        return removed;
    }

    public static Task<Tuple<Action, InternalCommunicationData<object>>?> CreateTaskFromAction(InternalContext context,
        Action action, string sessionName, ConcurrentBag<ActionFailure> actionFailures)
    {
        var task = new Task<Tuple<Action, InternalCommunicationData<object>>?>(() =>
            {
                try
                {
                    context.Logger.LogDebug("Starting action task {ActionType} {ActionName} in session {SessionName}",
                        action.GetType().Name, action.Name, sessionName);
                    return new Tuple<Action, InternalCommunicationData<object>>(action, action.Act());
                }
                catch (Exception e)
                {
                    // When action fails all actions that getting its results live will fail as well
                    var runningSession = context.GetRunningSession(sessionName);
                    runningSession.Inputs
                        ?.FirstOrDefault(input => input.Name == action.Name)?.DataCancellationTokenSource.Cancel();
                    runningSession.Outputs
                        ?.FirstOrDefault(output => output.Name == action.Name)?.DataCancellationTokenSource.Cancel();

                    var exceptionMessage =
                        e is OperationCanceledException ? $"Action {action.Name} was canceled" : e.Message;
                    actionFailures.AppendActionFailure(e, sessionName, context.Logger, action.GetType().Name,
                        action.Name, "", exceptionMessage);
                    return default;
                }
                finally
                {
                    context.Logger.LogDebug("Finished action task {ActionType} {ActionName} in session {SessionName}",
                        action.GetType().Name, action.Name, sessionName);
                }
            }
        );
        return task;
    }
}
