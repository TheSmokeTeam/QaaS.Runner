using Microsoft.Extensions.Logging;
using QaaS.Framework.Executions.Logics;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.ExecutionObjects;

namespace QaaS.Runner.Logics;

/// <summary>
/// Logic triggerer of DataSources in Executions runtime's pipeline
/// </summary>
public class DataSourceLogic(IList<DataSource> dataSources, InternalContext context) : ILogic
{
    /// <inheritdoc />
    public bool ShouldRun(ExecutionType executionType)
    {
        return executionType != ExecutionType.Template;
    }

    /// <summary>
    ///     Loads all DataSource to RunData and returns it.
    /// </summary>
    public ExecutionData Run(ExecutionData executionData)
    {
        context.Logger.LogInformation("Running {LogicName} Logic", "Data Sources");
        foreach (var source in dataSources)
            executionData.DataSources.Add(source);

        return executionData;
    }
}