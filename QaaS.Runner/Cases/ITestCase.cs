namespace QaaS.Runner.Cases;

/// <summary>
/// Defines a contract for custom test cases.
/// </summary>
public interface ITestCase
{
    /// <summary>
    /// Gets the logical case name stored on the execution context.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Configures the execution builder specifically for this test case.
    /// </summary>
    /// <param name="builder">The cloned execution builder for this case.</param>
    void SetupExecutionBuilder(ExecutionBuilder builder);
}
