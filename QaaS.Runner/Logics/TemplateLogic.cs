using QaaS.Framework.Configurations;
using QaaS.Framework.Executions.Logics;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;

namespace QaaS.Runner.Logics;

/// <summary>
/// Generates and writes a template YAML configuration.
/// </summary>
public class TemplateLogic(Context context, TextWriter? writer = null) : ILogic
{
    private TextWriter? _writer = writer;

    /// <summary>
    /// Builds and writes the framework template as YAML.
    /// </summary>
    /// <param name="executionData">The execution context passed through unchanged.</param>
    /// <returns>The same <paramref name="executionData" /> instance.</returns>
    public ExecutionData Run(ExecutionData executionData)
    {
        var template =
            context.RootConfiguration.BuildConfigurationAsYaml(Infrastructure.Constants.ConfigurationSectionNames);

        _writer ??= Console.Out;
        _writer?.WriteLine(template);
        return executionData;
    }
}
