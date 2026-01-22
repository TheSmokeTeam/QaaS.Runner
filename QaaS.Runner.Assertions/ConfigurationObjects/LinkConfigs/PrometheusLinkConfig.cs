using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;

public record PrometheusLinkConfig : ILinkConfig
{
    [Required]
    [Url]
    [Description("The prometheus' url, the base url without any route")]
    public string? Url { get; set; }

    [MinLength(1)]
    [Description("The expressions to generate prometheus panels for")]
    [DefaultValue(new[] { "" })]
    public string[] Expressions { get; set; } = { string.Empty };
}