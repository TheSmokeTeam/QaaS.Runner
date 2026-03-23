using System.ComponentModel;

namespace QaaS.Runner.Sessions.Session.Builders;

public class StageConfig
{
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
