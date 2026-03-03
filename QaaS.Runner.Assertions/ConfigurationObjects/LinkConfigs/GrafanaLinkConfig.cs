﻿using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;

/// <summary>
/// Configuration for Grafana dashboard links
/// </summary>
public record GrafanaLinkConfig : ILinkConfig
{
    /// <summary>
    /// The grafana's base URL without any route
    /// </summary>
    [Required]
    [Url]
    [Description("The grafana's url, the base url without any route")]
    public string? Url { get; set; }

    /// <summary>
    /// The ID of the desired dashboard to view
    /// </summary>
    [Required]
    [Description("The Id of the desired dashboard to view")]
    public string? DashboardId { get; set; }

    /// <summary>
    /// The variables to display the dashboard with
    /// </summary>
    [Description("The variables to display the dashboard with")]
    public KeyValuePair<string, string>[] Variables { get; set; } = [];
}