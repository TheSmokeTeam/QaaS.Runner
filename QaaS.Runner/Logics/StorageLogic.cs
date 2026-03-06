using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using MoreLinq.Extensions;
using QaaS.Framework.Executions.Logics;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Runner.Storage;

namespace QaaS.Runner.Logics;

/// <summary>
/// Stores or retrieves session data using configured storage providers.
/// </summary>
public class StorageLogic(IList<IStorage> storages, InternalContext context, ExecutionType executionType)
    : ILogic
{
    /// <summary>
    /// Stores or retrieves session data based on the execution mode provided to the logic instance.
    /// </summary>
    /// <param name="executionData">The mutable execution context containing session data.</param>
    /// <returns>The same <paramref name="executionData" /> instance after storage operations complete.</returns>
    public ExecutionData Run(ExecutionData executionData)
    {
        if (executionType == ExecutionType.Assert)
        {
            context.Logger.LogInformation("Running {LogicType} Logic", "Storages Retrieve");
            storages.ForEach(storage =>
                storage.Retrieve(context.CaseName).ForEach(sessionData =>
                {
                    // If session name is not already present in sessionData list add it to session data
                    if (executionData.SessionDatas.All(alreadySavedSessionData =>
                            alreadySavedSessionData?.Name != sessionData.Name))
                    {
                        executionData.SessionDatas.Add(sessionData);
                        context.Logger.LogDebug("Retrieved session data {SessionName} from storage {StorageType}",
                            sessionData.Name, storage.GetType().Name);
                    }

                    else
                        context.Logger.LogWarning("Session data {SessionName} was found more than once across storage providers. Keeping the first instance and skipping the duplicate from {StorageType}",
                            sessionData.Name, storage.GetType().Name);
                })
            );
            context.Logger.LogInformation("Finished retrieving session data from {StorageCount} storage provider(s)",
                storages.Count);
        }
        else
        {
            context.Logger.LogInformation("Running {LogicType} Logic", "Storages Store");
            storages.ForEach(storage =>
            {
                context.Logger.LogDebug("Persisting {SessionCount} session result(s) to storage {StorageType}",
                    executionData.SessionDatas.Count, storage.GetType().Name);
                storage.Store(executionData.SessionDatas.ToImmutableList(), context.CaseName);
            });
            context.Logger.LogInformation("Finished storing session data in {StorageCount} storage provider(s)",
                storages.Count);
        }

        return executionData;
    }
}
