using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QaaS.Runner.Sessions.ConfigurationObjects;

public record Parallel
{
    [Required]
    [Range(1, int.MaxValue)]
    [Description("The amount of parallel tasks to execute at once")]
    public int? Parallelism { get; init; }
}