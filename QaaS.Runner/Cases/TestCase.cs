using Microsoft.Extensions.Configuration;

namespace QaaS.Runner.Cases;

/// <summary>
/// Generic singleton base class for test cases matching the template design.
/// </summary>
/// <typeparam name="T">The concrete test case type.</typeparam>
public abstract class TestCase<T> : ITestCase where T : TestCase<T>, new()
{
    /// <summary>
    /// Gets the singleton instance of this test case.
    /// </summary>
    public static T Instance { get; } = new();

    /// <inheritdoc />
    public virtual string Name => GetType().Name;

    /// <summary>
    /// Modifies the execution builder when profile variables are not required.
    /// </summary>
    /// <param name="baseBuilder">The cloned execution builder for this case.</param>
    public virtual void ModifyCaseExecutionBuilder(ExecutionBuilder baseBuilder) { }

    /// <inheritdoc />
    public virtual void ModifyCaseExecutionBuilder(ExecutionBuilder baseBuilder, IConfiguration? variables = null)
    {
        ModifyCaseExecutionBuilder(baseBuilder);
    }
}

/// <summary>
/// Non-generic convenience base class for test cases.
/// </summary>
public abstract class TestCase : ITestCase
{
    /// <inheritdoc />
    public virtual string Name => GetType().Name;

    /// <summary>
    /// Modifies the execution builder when profile variables are not required.
    /// </summary>
    /// <param name="baseBuilder">The cloned execution builder for this case.</param>
    public virtual void ModifyCaseExecutionBuilder(ExecutionBuilder baseBuilder) { }

    /// <inheritdoc />
    public virtual void ModifyCaseExecutionBuilder(ExecutionBuilder baseBuilder, IConfiguration? variables = null)
    {
        ModifyCaseExecutionBuilder(baseBuilder);
    }
}
