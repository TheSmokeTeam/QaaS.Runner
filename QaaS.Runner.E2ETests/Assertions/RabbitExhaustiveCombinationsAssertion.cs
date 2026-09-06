using System.Collections.Immutable;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.E2ETests.Assertions;

public class RabbitExhaustiveCombinationsAssertion : BaseAssertion<object>
{
    public override bool Assert(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        if (sessionDataList == null || sessionDataList.Count == 0)
        {
            AssertionMessage = "No session data received for exhaustive RabbitMQ verification.";
            return false;
        }

        var allPassed = true;
        var failureReasons = new List<string>();
        var successSummaries = new List<string>();

        foreach (var session in sessionDataList)
        {
            var publishedPayloads = session.Inputs?
                .SelectMany(input => input.Data)
                .Select(data => data.Body)
                .OfType<MockJson>()
                .ToList() ?? [];

            var consumedPayloads = session.Outputs?
                .SelectMany(output => output.Data)
                .Select(data => data.Body)
                .OfType<MockJson>()
                .ToList() ?? [];

            var hasFailures = session.SessionFailures != null && session.SessionFailures.Count > 0;

            if (publishedPayloads.Count == 0)
            {
                if (hasFailures)
                {
                    allPassed = false;
                    failureReasons.Add($"Session '{session.Name}' encountered {session.SessionFailures!.Count} broker/execution failure(s).");
                }
                continue;
            }

            if (hasFailures)
            {
                allPassed = false;
                failureReasons.Add($"Session '{session.Name}' had {session.SessionFailures!.Count} broker failure(s).");
            }

            if (consumedPayloads.Count == 0)
            {
                allPassed = false;
                failureReasons.Add($"Session '{session.Name}' consumed 0 messages (expected {publishedPayloads.Count}).");
                continue;
            }

            var allMatched = publishedPayloads.All(pub =>
                consumedPayloads.Any(con => con.Property == pub.Property));

            if (!allMatched)
            {
                allPassed = false;
                var pubVals = string.Join(", ", publishedPayloads.Select(p => p.Property));
                var conVals = string.Join(", ", consumedPayloads.Select(c => c.Property));
                failureReasons.Add($"Session '{session.Name}' payload mismatch. Published: [{pubVals}], Consumed: [{conVals}]");
            }
            else
            {
                successSummaries.Add($"Session '{session.Name}' [OK: {consumedPayloads.Count}/{publishedPayloads.Count} msgs matched '{publishedPayloads.First().Property}']");
            }
        }

        AssertionMessage = allPassed
            ? $"All {successSummaries.Count} RabbitMQ 3.9 combination scenarios completed with 100% round-trip message consumption and zero broker errors."
            : $"Exhaustive combination assertion failed: {string.Join("; ", failureReasons)}";

        AssertionTrace = allPassed
            ? string.Join(Environment.NewLine, successSummaries)
            : string.Join(Environment.NewLine, failureReasons.Concat(successSummaries));

        return allPassed;
    }
}
