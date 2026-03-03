﻿using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;

/// <summary>
/// Configuration for Prometheus graph links
/// </summary>
public record PrometheusLinkConfig : ILinkConfig
{
    /// <summary>
    /// The prometheus' base URL without any route
    /// </summary>
    [Required]
    [Url]
    [Description("The prometheus' url, the base url without any route")]
    public string? Url { get; set; }

    /// <summary>
    /// The expressions to generate prometheus panels for
    /// </summary>
    [MinLength(1)]
    [Description("The expressions to generate prometheus panels for")]
    [DefaultValue(new[] { "" })]
    public string[] Expressions { get; set; } = { string.Empty };
}