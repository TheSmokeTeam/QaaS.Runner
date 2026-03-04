using QaaS.Framework.SDK.AssertionObjects;
using QaaS.Framework.SDK.Hooks.Assertion;

namespace QaaS.Runner.Assertions.AssertionObjects;

/// <summary>
///     Contains all relevant data to the result of an assertion
/// </summary>
public record AssertionResult : IAssertionResult
{
    public Assertion Assertion { get; set; }

    /// <summary>
    ///     The assertion's execution status
    /// </summary>
    public AssertionStatus AssertionStatus { get; init; }

    /// <summary>
    ///     How long in milliseconds did the full assertion's test take (includes all relevant test sessions + its assertion)
    /// </summary>
    public long TestDurationMs { get; init; }

    /// <summary>
    ///     If the assertion threw an exception its saved here
    /// </summary>
    public Exception? BrokenAssertionException { get; init; }

    /// <summary>
    ///     Links to attach to the test results
    /// </summary>
    public IEnumerable<KeyValuePair<string, string>>? Links { get; init; }

    /// <summary>
    ///     Contain data about the flakiness of the assertion
    /// </summary>
    public Flaky Flaky { get; init; }
}