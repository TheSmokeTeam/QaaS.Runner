using QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;

namespace QaaS.Runner.Assertions.LinkBuilders;

/// <summary>
///     Builds a link to a grafana dashboard
/// </summary>
public class GrafanaLink(string linkName, GrafanaLinkConfig grafanaLinkConfig) : BaseLink(linkName)
{
    private const string StartTimeVariableKey = "from", EndTimeVariableKey = "to", DashboardAnnotation = "d";

    /// <inheritdoc />
    protected override string BuildLink(IList<KeyValuePair<DateTime, DateTime>> startEndTimesKeyValuePairs)
    {
        var testLatestEndTime =
            new DateTimeOffset(startEndTimesKeyValuePairs.Max(pair => pair.Value)).ToUnixTimeMilliseconds();
        var testEarliestStartTime =
            new DateTimeOffset(startEndTimesKeyValuePairs.Min(pair => pair.Key)).ToUnixTimeMilliseconds();

        var variablesAsString = string.Join("&", grafanaLinkConfig.Variables.Select(var =>
            $"{var.Key}={var.Value}"));

        return new UriBuilder($"{grafanaLinkConfig.Url!}/{DashboardAnnotation}/{grafanaLinkConfig.DashboardId!}")
        {
            Query =
                $"{StartTimeVariableKey}={testEarliestStartTime}&{EndTimeVariableKey}={testLatestEndTime}&{variablesAsString}"
        }.ToString();
    }
}