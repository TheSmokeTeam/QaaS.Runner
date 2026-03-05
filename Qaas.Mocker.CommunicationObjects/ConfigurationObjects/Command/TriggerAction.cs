using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;

[ExcludeFromCodeCoverage]
public record TriggerAction
{
    [Required, Description("The Action's name that is being triggered")]
    public string? ActionName { get; init; }

    [DefaultValue(0), Description("The time to enable the action for in milliseconds")]
    public int TimeoutMs { get; init; } = 0;
}