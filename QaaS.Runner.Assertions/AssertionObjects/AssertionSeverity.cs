namespace QaaS.Runner.Assertions.AssertionObjects;

/// <summary>
///     The severity of an assertion, its importance.
/// </summary>
public enum AssertionSeverity
{
    /// <summary>
    ///     Least important assertion severity.
    /// </summary>
    Trivial,

    /// <summary>
    ///     Somewhat important assertion severity.
    /// </summary>
    Minor,

    /// <summary>
    ///     Normal importance assertion severity.
    /// </summary>
    Normal,

    /// <summary>
    ///     Very important assertion severity.
    /// </summary>
    Critical,

    /// <summary>
    ///     Most important assertion severity.
    /// </summary>
    Blocker
}