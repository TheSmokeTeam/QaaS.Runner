using AssertionResult = QaaS.Runner.Assertions.AssertionObjects.AssertionResult;

namespace QaaS.Runner.Assertions.Reporters;

/// <summary>
/// Interface for a reporter that can write test results to a specific target (e.g., Allure, ReportPortal).
/// </summary>
public interface IReporter
{
    /// <summary>
    /// Weather to save session data. If not set, each assertion will determine whether to save it.
    /// </summary>
    public bool? SaveSessionData { get; set; }
    
    /// <summary>
    /// Weather to save attachments. If not set, each assertion will determine whether to save them.
    /// </summary>
    public bool? SaveAttachments { get; set; }
    
    /// <summary>
    /// Weather to save logs. If not set, each assertion will determine whether to save them.
    /// </summary>
    public bool? SaveLogs { get; set; }
    
    /// <summary>
    /// Weather to save the configuration template. If not set, each assertion will determine whether to save it.
    /// </summary>
    public bool? SaveTemplate { get; set; }
    
    /// <summary>
    /// Weather to display the assertion message trace. If not set, each assertion will determine whether to display it.
    /// </summary>
    public bool? DisplayTrace { get; set; }
    
    /// <summary>
    /// The epoch time in milliseconds when the test suite started. This can be used for reporting purposes to indicate when the test suite execution began.
    /// </summary>
    public long EpochTestSuiteStartTime { get; set; }

    /// <summary>
    /// Writes the test results to the reporter's target.
    /// </summary>
    /// <param name="assertionResult">The result of the assertion to be reported.</param>
    public void WriteTestResults(AssertionResult assertionResult);
}
