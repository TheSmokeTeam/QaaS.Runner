namespace QaaS.Runner.Sessions.Session.Builders;

public class StageConfig
{
    public StageConfig(int stageNumber, int? timeoutBefore = null, int? timeoutAfter = null)
    {
        StageNumber = stageNumber;
        TimeoutBefore = timeoutBefore;
        TimeoutAfter = timeoutAfter;
    }

    internal int StageNumber { get; set; }
    internal int? TimeoutBefore { get; set; }
    internal int? TimeoutAfter { get; set; }
}