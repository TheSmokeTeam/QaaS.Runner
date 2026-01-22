using QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;

namespace QaaS.Runner.Assertions.LinkBuilders;

/// <summary>
///     Builds a kibana discovery link
/// </summary>
public class KibanaLink(string linkName, KibanaLinkConfig kibanaLinkConfig) : BaseLink(linkName)
{
    private const string DiscoveryRoute = "/app/discover",
        NoRefreshRefreshIntervalAnnotation = "refreshInterval:(pause:!f,value:0)",
        TimeRangeTemplate = "time:(from:'{0}',to:'{1}')",
        BetweenDatesQueryTemplate = "({0} >= \"{1}\" and {0} <= \"{2}\")";

    /// <inheritdoc />
    protected override string BuildLink(IList<KeyValuePair<DateTime, DateTime>> startEndTimesKeyValuePairs)
    {
        var startEndTimesKeyValuePairsArray = startEndTimesKeyValuePairs.ToArray();

        var timeRange = string.Format(TimeRangeTemplate,
            startEndTimesKeyValuePairsArray.Min(pair => pair.Key).ToString("o"),
            startEndTimesKeyValuePairsArray.Max(pair => pair.Value).ToString("o"));

        var betweenDatesQuery = string.Join(" or ", startEndTimesKeyValuePairsArray.Select(pair =>
            string.Format(BetweenDatesQueryTemplate,
                Uri.EscapeDataString(kibanaLinkConfig.TimestampField),
                pair.Key.ToString("o"), pair.Value.ToString("o"))));

        var kqlQuery = kibanaLinkConfig.KqlQuery is not null
            ? $"and ({Uri.EscapeDataString(kibanaLinkConfig.KqlQuery)})"
            : string.Empty;

        return $"{kibanaLinkConfig.Url!}{DiscoveryRoute}#/?_g=({NoRefreshRefreshIntervalAnnotation},{timeRange})" +
               $"&_a=(index:'{Uri.EscapeDataString(kibanaLinkConfig.DataViewId!)}'," +
               $"query:(language:kuery,query:'({betweenDatesQuery}) {kqlQuery}'))";
    }
}