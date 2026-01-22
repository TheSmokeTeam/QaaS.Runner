using QaaS.Framework.Configurations;
using QaaS.Framework.Executions.Logics;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;

namespace QaaS.Runner.Logics;

/// <summary>
/// Logic class for template command
/// </summary>
public class TemplateLogic(Context context, TextWriter? writer = null) : ILogic
{
    private TextWriter? _writer = writer;

    /// <inheritdoc />
    public bool ShouldRun(ExecutionType executionType)
    {
        return executionType == ExecutionType.Template;
    }

    /// <summary>
    /// Outputs the configured objects 
    /// </summary>
    public ExecutionData Run(ExecutionData executionData)
    {
        var template =
            context.RootConfiguration.BuildConfigurationAsYaml(Infrastructure.Constants.ConfigurationSectionNames);

        _writer ??= Console.Out;
        _writer?.WriteLine(template);
        return executionData;
    }
}