﻿using AssertionResult = QaaS.Runner.Assertions.AssertionObjects.AssertionResult;

namespace QaaS.Runner.Assertions;

/// <summary>
/// Interface for test result reporters
/// </summary>
public interface IReporter
{
    /// <summary>
    /// Name of the reporter
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Whether to save session data in the report
    /// </summary>
    public bool SaveSessionData { get; set; }

    /// <summary>
    /// Whether to save attachments in the report
    /// </summary>
    public bool SaveAttachments { get; set; }

    /// <summary>
    /// Whether to display assertion trace in the report
    /// </summary>
    public bool DisplayTrace { get; set; }

    /// <summary>
    /// Epoch timestamp of when the test suite started
    /// </summary>
    public long EpochTestSuiteStartTime { get; set; }

    /// <summary>
    /// Writes test results to the report
    /// </summary>
    /// <param name="assertionResult">The assertion result to write</param>
    public void WriteTestResults(AssertionResult assertionResult);
}