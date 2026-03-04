using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.Assertions.AssertionObjects;

/// <summary>
///     Contain data about the flakiness of assertion
/// </summary>
public record Flaky
{
    /// <summary>
    ///     Is the assertion flaky
    /// </summary>
    public bool IsFlaky { get; init; }

    /// <summary>
    ///     The reasons the assertion is flaky, each key is the Name of the session who caused the flakiness
    ///     and the value is the failures in that session
    /// </summary>
    public IList<KeyValuePair<string, List<ActionFailure>>> FlakinessReasons { get; init; } = null!;
}