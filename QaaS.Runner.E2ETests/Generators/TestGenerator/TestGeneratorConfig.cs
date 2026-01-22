using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QaaS.Runner.E2ETests.Generators.TestGenerator;

public class TestGeneratorConfig
{
    [Range(1, int.MaxValue)]
    [Description("The amount of messages to generate")]
    public int? Count { get; set; }

    [Description("Some complex config for testing purposes")]
    public List<ComplexConfig> ComplexConfigList { get; set; }
}

public class ComplexConfig
{
    public string SomeItem { get; set; }
}