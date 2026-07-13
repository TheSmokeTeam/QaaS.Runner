using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.Assertions;

/// <summary>
/// Provides deterministic action-failure projections for assertions and reporters, including restored legacy data.
/// </summary>
internal static class ActionFailureNormalizer
{
    public static List<ActionFailure> Normalize(IEnumerable<ActionFailure> actionFailures)
    {
        ArgumentNullException.ThrowIfNull(actionFailures);

        return actionFailures
            .GroupBy(CreateFailureKey)
            .Select(group => group.First())
            .OrderBy(failure => failure.Action ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(failure => failure.ActionType, StringComparer.Ordinal)
            .ThenBy(failure => failure.Name, StringComparer.Ordinal)
            .ThenBy(failure => failure.Reason.Message, StringComparer.Ordinal)
            .ThenBy(failure => failure.Reason.Description, StringComparer.Ordinal)
            .ToList();
    }

    private static FailureKey CreateFailureKey(ActionFailure actionFailure) =>
        new(
            actionFailure.Action ?? string.Empty,
            actionFailure.ActionType,
            actionFailure.Name,
            actionFailure.Reason.Message,
            actionFailure.Reason.Description
        );

    private sealed record FailureKey(
        string Action,
        string ActionType,
        string Name,
        string Message,
        string Description
    );
}
