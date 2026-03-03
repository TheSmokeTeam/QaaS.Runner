using System.ComponentModel.DataAnnotations;

namespace QaaS.Runner.E2ETests.Generators.FromConsumerGenerator;

public record FromConsumerGeneratorConfig
{
    [Required] public string SessionName { get; set; } = string.Empty;
    [Required] public string ConsumerName { get; set; } = string.Empty;
}
