using System.Collections.Concurrent;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.Sessions.Session;

/// <summary>
/// Represents an executable session within the runtime stage pipeline.
/// </summary>
public interface ISession
{
    /// <summary>
    /// Gets the session identifier used in logs and resulting session data.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the stage number that this session blocks before completion should be awaited.
    /// </summary>
    /// <remarks>
    /// When <see langword="null" />, the session result is collected in its own stage.
    /// </remarks>
    public int? RunUntilStage { get; }

    /// <summary>
    /// Gets the stage number in which this session starts execution.
    /// </summary>
    public int SessionStage { get; }

    /// <summary>
    /// Executes the session using the provided execution context.
    /// </summary>
    /// <param name="executionData">Runtime execution state containing session and datasource inputs.</param>
    /// <returns>The produced session data, or <see langword="null" /> when no data is produced.</returns>
    public SessionData? Run(ExecutionData executionData);
}
