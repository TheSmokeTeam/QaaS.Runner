using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using QaaS.Framework.Configurations.CustomValidationAttributes;

namespace QaaS.Runner.ConfigurationObjects;

[ExcludeFromCodeCoverage]
public record ExecuteConfigurations
{
    [Required]
    [MinLength(1)]
    [UniquePropertyInEnumerable(nameof(CommandConfig.Id))]
    [Description("The list of QaaS commands to execute in the order they will be executed")]
    internal CommandConfig[]? Commands { get; set; }

    public IReadOnlyList<CommandConfig> ReadCommands() => Commands ?? [];
}
