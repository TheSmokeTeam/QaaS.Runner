using System.Collections.Immutable;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.E2ETests.Assertions;

public class RabbitRoundTripAssertion : BaseAssertion<object>
{
    public override bool Assert(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        var session = sessionDataList.SingleOrDefault();
        var publishedPayloads = session?.Inputs?
            .SelectMany(input => input.Data)
            .Select(data => data.Body)
            .OfType<MockJson>()
            .ToList() ?? [];
        var consumedPayloads = session?.Outputs?
            .SelectMany(output => output.Data)
            .Select(data => data.Body)
            .OfType<MockJson>()
            .ToList() ?? [];

        var matchedPayload = publishedPayloads.FirstOrDefault()?.Property;
        var passed = matchedPayload != null && consumedPayloads.Any(payload => payload.Property == matchedPayload);

        AssertionMessage = passed
            ? "RabbitMQ round-trip completed successfully."
            : "Expected the consumer to receive the published RabbitMQ payload.";
        AssertionTrace = passed
            ? $"Published and consumed payload: {matchedPayload}"
            : $"Published={publishedPayloads.Count}, Consumed={consumedPayloads.Count}";
        return passed;
    }
}
