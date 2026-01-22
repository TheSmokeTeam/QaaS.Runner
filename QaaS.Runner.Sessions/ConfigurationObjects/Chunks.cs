using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QaaS.Runner.Sessions.ConfigurationObjects;

public record Chunks
{
    [Required]
    [Description("The size of the chunk to use")]
    public int? ChunkSize { get; set; }
}