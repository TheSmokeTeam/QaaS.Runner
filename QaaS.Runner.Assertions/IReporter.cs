using AssertionResult = QaaS.Runner.Assertions.AssertionObjects.AssertionResult;

namespace QaaS.Runner.Assertions;

public interface IReporter
{
    public ReporterKind Kind { get; }
    public string Name { get; set; }
    public string AssertionName { get; set; }

    public bool SaveSessionData { get; set; }

    public bool SaveAttachments { get; set; }

    public bool SaveLogs { get; set; }

    public bool SaveTemplate { get; set; }

    public bool DisplayTrace { get; set; }

    public long EpochTestSuiteStartTime { get; set; }

    public void WriteTestResults(AssertionResult assertionResult);
}
