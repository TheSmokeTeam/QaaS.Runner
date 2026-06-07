namespace QaaS.Runner.Cases;

/// <summary>
/// A convenience abstract base class implementing <see cref="ITestCase"/>.
/// </summary>
public abstract class TestCase : ITestCase
{
    /// <inheritdoc />
    public abstract void SetupExecutionBuilder(ExecutionBuilder builder);
}
