using QaaS.Runner.Assertions.AssertionObjects;
using AssertionResult = QaaS.Runner.Assertions.AssertionObjects.AssertionResult;

namespace QaaS.Runner.Assertions.Reporters;

public interface IReporter
{
    public ReporterTarget Target { get; set; }

    public string Name { get; set; }
    public string AssertionName { get; set; }

    public bool SaveSessionData { get; set; }

    public bool SaveAttachments { get; set; }
    
    public bool SaveLogs { get; set; }

    public bool DisplayTrace { get; set; }

    public DateTime EpochTestSuiteStartTime { get; set; }

    public void WriteTestResults(AssertionResult assertionResult);
}
