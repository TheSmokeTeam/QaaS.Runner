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
    internal int StageNumber { get; set; }

    [Description("Optional time in milliseconds to wait before starting this internal session stage.")]
    internal int? TimeoutBefore { get; set; }

    [Description("Optional time in milliseconds to wait after this internal session stage completes.")]
    internal int? TimeoutAfter { get; set; }
}
