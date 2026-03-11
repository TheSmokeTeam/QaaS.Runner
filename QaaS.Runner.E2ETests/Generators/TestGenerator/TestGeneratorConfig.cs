using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QaaS.Runner.E2ETests.Generators.TestGenerator;

public class TestGeneratorConfig
{
    [Range(1, int.MaxValue)]
    [Description("The amount of messages to generate")]
    public int Count { get; set; } = 1;
}
