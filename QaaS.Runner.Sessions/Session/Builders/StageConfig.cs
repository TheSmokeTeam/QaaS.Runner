using System.ComponentModel;

namespace QaaS.Runner.Sessions.Session.Builders;

/// <summary>
/// Configures stage-level timing around a session stage.
/// </summary>
public class StageConfig
{
    /// <summary>
    /// Creates an empty stage configuration for configuration binding scenarios.
    /// </summary>
    /// <remarks>
    /// The runner binds YAML and configuration objects through reflection-based construction. A
    /// parameterless constructor keeps that binding path compatible without changing the public API
    /// used by existing callers that already construct stages with explicit values.
    /// </remarks>
    public StageConfig()
    {
    }

    /// <summary>
    /// Creates a stage configuration with an explicit stage number and optional delays.
    /// </summary>
    public StageConfig(int stageNumber, int? timeoutBefore = null, int? timeoutAfter = null)
    {
        StageNumber = stageNumber;
        TimeoutBefore = timeoutBefore;
        TimeoutAfter = timeoutAfter;
    }

    [Description("The internal session stage number this configuration applies to.")]
    public int StageNumber { get; internal set; }
    [Description("Optional time in milliseconds to wait before starting this internal session stage.")]
    public int? TimeoutBefore { get; internal set; }
    [Description("Optional time in milliseconds to wait after this internal session stage completes.")]
    public int? TimeoutAfter { get; internal set; }
}
