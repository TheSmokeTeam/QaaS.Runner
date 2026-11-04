using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QaaS.Runner.Sessions.ConfigurationObjects;

public record Parallel
{
    [Required]
    [Range(1, int.MaxValue)]
    [Description("Maximum concurrent workers within one publisher or transaction action. A chunk publisher assigns one chunk to each worker. Each action uses its own worker limit; protocol connection limits can further serialize operations.")]
    public int? Parallelism { get; init; }
}