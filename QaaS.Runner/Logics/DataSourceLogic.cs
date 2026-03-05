using Microsoft.Extensions.Logging;
using QaaS.Framework.Executions.Logics;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.ExecutionObjects;

namespace QaaS.Runner.Logics;

/// <summary>
/// Loads configured data sources into the runtime execution context.
/// </summary>
public class DataSourceLogic(IList<DataSource> dataSources, InternalContext context) : ILogic
{
    /// <summary>
    /// Determines whether data sources should be loaded for the requested execution type.
    /// </summary>
    /// <param name="executionType">The active execution pipeline mode.</param>
    /// <returns>
    /// <see langword="true" /> for all execution types except <see cref="ExecutionType.Template" />.
    /// </returns>
    public bool ShouldRun(ExecutionType executionType)
    {
        return executionType != ExecutionType.Template;
    }

    /// <summary>
    /// Appends all configured data sources to <see cref="ExecutionData.DataSources" />.
    /// </summary>
    /// <param name="executionData">The mutable execution context to populate.</param>
    /// <returns>The same <paramref name="executionData" /> instance with loaded data sources.</returns>
    public ExecutionData Run(ExecutionData executionData)
    {
        context.Logger.LogInformation("Running {LogicName} Logic", "Data Sources");
        foreach (var source in dataSources)
            executionData.DataSources.Add(source);

        return executionData;
    }
}
