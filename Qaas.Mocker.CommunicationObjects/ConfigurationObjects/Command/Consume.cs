using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using QaaS.Framework.SDK.Session;

namespace Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;

[ExcludeFromCodeCoverage]
public record Consume
{ 
    [Required, Description("The Timeout (ms) while consuming data from Mock Servers"), Range(0, int.MaxValue)]
    public int TimeoutMs { get; init; }
    
    [Description("The Action name to consume, if not given consumes all action")]
    public string? ActionName { get; init; }

    [Description("How to filter the properties of each returned Mocker Input data")]
    public DataFilter InputDataFilter { get; init; } = new();
    
    [Description("How to filter the properties of each returned Mocker Output data")]
    public DataFilter OutputDataFilter { get; init; } = new();
}