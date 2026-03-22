namespace QaaS.Runner;

/// <summary>
/// Adds code-first configuration to an <see cref="ExecutionBuilder"/> discovered at runtime.
/// Implementations in the entry assembly or copied output dependencies are applied automatically
/// after YAML configuration has been loaded.
/// </summary>
public interface IExecutionBuilderConfigurator
{
    /// <summary>
    /// Mutates the provided execution builder with additional or replacement configuration.
    /// </summary>
    /// <param name="executionBuilder">The execution builder to configure.</param>
    void Configure(ExecutionBuilder executionBuilder);
}
