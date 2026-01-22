using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using QaaS.Framework.Configurations.CustomValidationAttributes;

namespace QaaS.Runner.ConfigurationObjects;

[ExcludeFromCodeCoverage]
public record CommandConfig
{
    [Required]
    [Description("The QaaS command to execute")]
    public string? Command { get; set; }

    [Required]
    [ValidPath]
    [MinLength(1)]
    [MaxLength(100)]
    [Description("A unique identifier to identify the command by")]
    public string? Id { get; set; }
}