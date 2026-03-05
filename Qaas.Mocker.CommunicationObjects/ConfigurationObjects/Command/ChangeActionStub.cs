using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;

[ExcludeFromCodeCoverage]
public record ChangeActionStub
{
    [Required, Description("The Action's name that is being changed")]
    public string ActionName { get; init; } = string.Empty;
    
    [Required, Description("The Stub's Name attached to the action")]
    public string StubName { get; init; } = string.Empty;
}
