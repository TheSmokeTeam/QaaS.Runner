using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QaaS.Runner.E2ETests.Generators.ReportPortal;

/// <summary>
/// Configures the sample data emitted by the ReportPortal E2E payload generator.
/// </summary>
public sealed class ReportPortalPayloadGeneratorConfig
{
    [Range(1, 100)]
    [Description("How many sample data items to generate.")]
    public int Count { get; set; } = 3;

    [Description("Prefix used to build deterministic payload IDs.")]
    public string PayloadPrefix { get; set; } = "reportportal-payload";

    [Description("Component label baked into the payload metadata.")]
    public string Component { get; set; } = "General";

    [Description("Area label baked into the payload metadata.")]
    public string Area { get; set; } = "Core";

    [Description("Owner label baked into the payload metadata.")]
    public string Owner { get; set; } = "QaaS";

    [Description("Environment label included in the generated payload body.")]
    public string EnvironmentName { get; set; } = "Lab";

    [Description("Capability label included in the generated payload body.")]
    public string Capability { get; set; } = "Baseline";

    [Description("Region label included in the generated payload body.")]
    public string Region { get; set; } = "IL";

    [Description("Scenario label included in the generated payload body.")]
    public string Scenario { get; set; } = "Baseline";

    [Range(1, 10)]
    [Description("How many evidence entries to generate per payload item.")]
    public int EvidenceCount { get; set; } = 2;

    [Description("Additional free-form tags written into every generated payload item.")]
    public List<string> Tags { get; set; } =
    [
        "QaaS",
        "ReportPortal",
        "E2E"
    ];

    [Description("Additional notes written into every generated payload item.")]
    public List<string> Notes { get; set; } = [];
}
