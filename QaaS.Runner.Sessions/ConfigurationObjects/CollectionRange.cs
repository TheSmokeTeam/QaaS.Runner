using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace QaaS.Runner.Sessions.ConfigurationObjects;

public record CollectionRange
{
    [Range(int.MinValue, int.MaxValue)]
    [Description(
        "The start time of the collection range in relation to the session start time, given in milliseconds," +
        " is added to the session's start time as is to determine the collection start time.")]
    [DefaultValue(0)]
    public int StartTimeMs { get; set; } = 0;

    [Range(int.MinValue, int.MaxValue)]
    [Description("The end time of the collection range in relation to the session end time, given in milliseconds, " +
                 "is added to the session's start time as is to determine the collection end time.")]
    [DefaultValue(0)]
    public int EndTimeMs { get; set; } = 0;
}