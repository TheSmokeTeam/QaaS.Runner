using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;

public record GrafanaLinkConfig : ILinkConfig
{
    [Required]
    [Url]
    [Description("The grafana's url, the base url without any route")]
    public string? Url { get; set; }

    [Required]
    [Description("The Id of the desired dashboard to view")]
    public string? DashboardId { get; set; }

    [Description("The variables to display the dashboard with")]
    public KeyValuePair<string, string>[] Variables { get; set; } = [];
}