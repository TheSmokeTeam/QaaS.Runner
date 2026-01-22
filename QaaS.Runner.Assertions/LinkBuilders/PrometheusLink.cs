using QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;

namespace QaaS.Runner.Assertions.LinkBuilders;

/// <summary>
///     Builds a prometheus graph link
/// </summary>
public class PrometheusLink(string linkName, PrometheusLinkConfig prometheusLinkConfig) : BaseLink(linkName)
{
    private const string
        GraphRoute = "/graph",
        AnnotationPrefixTemplate = "g{0}.",
        GraphViewAnnotation = "tab=0",
        ExpressionAnnotation = "expr",
        TimeRangeAnnotation = "range_input",
        EndTimeAnnotation = "end_input";

    private const char AnnotationSeparatorChar = '&';

    /// <inheritdoc />
    protected override string BuildLink(IList<KeyValuePair<DateTime, DateTime>> startEndTimesKeyValuePairs)
    {
        var testLatestEndTime = startEndTimesKeyValuePairs.Max(pair => pair.Value);
        var timeBetweenLatestEndTimeAndFirstStartTimeMs = (long)Math.Round(
            (testLatestEndTime - startEndTimesKeyValuePairs.Min(pair => pair.Key)).TotalMilliseconds,
            MidpointRounding.AwayFromZero);

        var expressionCounter = 0;
        var expressionsString = string.Join(AnnotationSeparatorChar,
            prometheusLinkConfig.Expressions.Select(expr =>
            {
                var annotationsString =
                    $"{string.Format(AnnotationPrefixTemplate, expressionCounter)}{ExpressionAnnotation}={Uri.EscapeDataString(expr)}{AnnotationSeparatorChar}" +
                    $"{string.Format(AnnotationPrefixTemplate, expressionCounter)}{GraphViewAnnotation}{AnnotationSeparatorChar}" +
                    $"{string.Format(AnnotationPrefixTemplate, expressionCounter)}{TimeRangeAnnotation}={timeBetweenLatestEndTimeAndFirstStartTimeMs}ms{AnnotationSeparatorChar}" +
                    $"{string.Format(AnnotationPrefixTemplate, expressionCounter)}{EndTimeAnnotation}={testLatestEndTime:O}{AnnotationSeparatorChar}";
                expressionCounter++;
                return annotationsString;
            }));

        return $"{prometheusLinkConfig.Url!}{GraphRoute}?{expressionsString}";
    }
}