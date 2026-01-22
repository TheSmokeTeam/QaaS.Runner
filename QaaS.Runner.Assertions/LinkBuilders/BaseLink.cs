namespace QaaS.Runner.Assertions.LinkBuilders;

/// <summary>
///     Generic interface for classes that build link to put in the test result reports
/// </summary>
public abstract class BaseLink
{
    /// <summary>
    ///     The link's name
    /// </summary>
    private readonly string _linkName;

    /// <summary>
    ///     Base constructor, requires the name of the link
    /// </summary>
    protected BaseLink(string linkName)
    {
        _linkName = linkName;
    }

    /// <summary>
    ///     Build the link string and gives it a name
    ///     The key is the link's name and the value is the link itself
    /// </summary>
    /// <param name="startEndTimesKeyValuePairs">
    ///     An enumerable of time range filters to view items in the link only in the range given
    ///     Each key value pair is a time range where the key is the start time and the value is the end time
    ///     Also uses the minimum key and maximum value to find the correct time range to present the date view in
    /// </param>
    /// <returns></returns>
    public KeyValuePair<string, string> GetLink(IList<KeyValuePair<DateTime, DateTime>> startEndTimesKeyValuePairs)
    {
        return new KeyValuePair<string, string>(_linkName, BuildLink(startEndTimesKeyValuePairs));
    }

    /// <summary>
    ///     Builds the link string
    /// </summary>
    protected abstract string BuildLink(IList<KeyValuePair<DateTime, DateTime>> startEndTimesKeyValuePairs);
}