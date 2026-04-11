using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QaaS.Runner.E2ETests.Probes.ReportPortal;

/// <summary>
/// Configures the ReportPortal E2E diagnostic probe.
/// </summary>
public sealed class ReportPortalDiagnosticProbeConfig
{
    [Description("Human-readable probe name included in the failure message when the probe is configured to fail.")]
    public string StepName { get; set; } = "ReportPortal Diagnostic Probe";

    [Description("Free-form text written into the failure message or execution log.")]
    public string TraceMessage { get; set; } = "ReportPortal probe executed.";

    [Range(0, 5000)]
    [Description("Optional delay used to create more realistic session timing variation in the ReportPortal E2E runs.")]
    public int DelayMilliseconds { get; set; }

    [Description("Additional notes appended to the failure message when the probe is configured to fail.")]
    public List<string> Notes { get; set; } = [];

    [Description("When true the probe throws to create a session action failure for E2E validation.")]
    public bool ThrowOnRun { get; set; }
}
