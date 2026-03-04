using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;

public record KibanaLinkConfig : ILinkConfig
{
    [Required]
    [Url]
    [Description("The kibana's url, the base url without any route")]
    public string? Url { get; set; }

    [Required]
    [Description("The Id of the desired data view to view")]
    public string? DataViewId { get; set; }

    [Description("The name of the main timestamp field to use to query on specific times in given data view")]
    [DefaultValue("@timestamp")]
    public string TimestampField { get; set; } = "@timestamp";

    [Description("A custom Kql query to add to the generated URL" +
                 ", this query is added to the session time filtering query with `and` ")]
    public string? KqlQuery { get; set; }
}