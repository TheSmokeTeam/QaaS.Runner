﻿using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;

/// <summary>
/// Configuration for Kibana discovery links
/// </summary>
public record KibanaLinkConfig : ILinkConfig
{
    /// <summary>
    /// The kibana's base URL without any route
    /// </summary>
    [Required]
    [Url]
    [Description("The kibana's url, the base url without any route")]
    public string? Url { get; set; }

    /// <summary>
    /// The ID of the desired data view to query
    /// </summary>
    [Required]
    [Description("The Id of the desired data view to view")]
    public string? DataViewId { get; set; }

    /// <summary>
    /// The name of the main timestamp field to use for time filtering
    /// </summary>
    [Description("The name of the main timestamp field to use to query on specific times in given data view")]
    [DefaultValue("@timestamp")]
    public string TimestampField { get; set; } = "@timestamp";

    /// <summary>
    /// A custom KQL query to add to the generated URL
    /// </summary>
    [Description("A custom Kql query to add to the generated URL" +
                 ", this query is added to the session time filtering query with `and` ")]
    public string? KqlQuery { get; set; }
}