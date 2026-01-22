using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
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

        logger.LogDebug("Disposed of {EnumerableLength} {EnumerableName}",
            array.Length, enumerableName);
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
        var failedActionDescription = (actionProtocol != null ? $" {actionProtocol}" : "") + actionRuntimeName;
        logger.LogError(
            "{ActionType} {FailedAction} in {SessionName} failed due to the following exception \n{Exception}",
            actionType, failedActionDescription, sessionName, exception);

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
        var failedActionDescription = actionProtocol + actionRuntimeName;
        logger.LogError(
            "{ActionType} {FailedAction} in {SessionName} failed due to the following exception \n{Exception}",
            actionType, failedActionDescription, sessionName, exception);

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

    public static Task<Tuple<Action, InternalCommunicationData<object>>?> CreateTaskFromAction(InternalContext context,
        Action action, string sessionName, ConcurrentBag<ActionFailure> actionFailures)
    {
        var task = new Task<Tuple<Action, InternalCommunicationData<object>>?>(() =>
            {
                try
                {
                    return new Tuple<Action, InternalCommunicationData<object>>(action, action.Act());
                }
                catch (Exception e)
                {
                    // When action fails all actions that getting its results live will fail as well
                    context.InternalRunningSessions.RunningSessionsDict[sessionName].Inputs
                        ?.FirstOrDefault(input => input.Name == action.Name)?.DataCancellationTokenSource.Cancel();
                    context.InternalRunningSessions.RunningSessionsDict[sessionName].Outputs
                        ?.FirstOrDefault(output => output.Name == action.Name)?.DataCancellationTokenSource.Cancel();

                    var exceptionMessage =
                        e is OperationCanceledException ? $"Action {action.Name} was canceled" : e.Message;
                    actionFailures.AppendActionFailure(e, sessionName, context.Logger, action.GetType().ToString(),
                        action.Name, "", exceptionMessage);
                    return default;
                }
            }
        );
        return task;
    }
}