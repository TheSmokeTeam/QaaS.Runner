using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QaaS.Runner.Sessions.ConfigurationObjects;

public record Chunks
{
    [Description(
        "Optional maximum serialized body bytes per chunk, in addition to ChunkSize. Byte arrays use their length, strings use UTF-8 and structured bodies use JSON size. Excludes protocol envelopes and total session retention. A single oversized body fails rather than exceeding the limit. Omitted preserves count-only batching."
    )]
    [Range(typeof(long), "1", "9223372036854775807")]
    public long? MaxBytes { get; set; }

    [Required]
    [Description("The size of the chunk to use")]
    public int? ChunkSize { get; set; }
}
