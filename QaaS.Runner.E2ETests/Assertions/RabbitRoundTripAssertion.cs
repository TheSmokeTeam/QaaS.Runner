using System.Collections.Immutable;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.E2ETests.Probes;

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
        var observedProbeMarkers = ProbeRunRecorder.GetMarkers();
        var expectedProbeMarkers = new[] { "rabbit-roundtrip-probe", "probe-scope-check" };

        var matchedPayload = publishedPayloads.FirstOrDefault()?.Property;
        var payloadPassed = matchedPayload != null && consumedPayloads.Any(payload => payload.Property == matchedPayload);
        var probesPassed = expectedProbeMarkers.All(marker => observedProbeMarkers.Contains(marker));
        var passed = payloadPassed && probesPassed;

        AssertionMessage = passed
            ? "RabbitMQ round-trip completed successfully and scoped probes used their own configuration."
            : "Expected the consumer to receive the published RabbitMQ payload and each scoped probe to record its own marker.";
        AssertionTrace = passed
            ? $"Published and consumed payload: {matchedPayload}; ProbeMarkers=[{string.Join(", ", observedProbeMarkers)}]"
            : $"Published={publishedPayloads.Count}, Consumed={consumedPayloads.Count}, ProbeMarkers=[{string.Join(", ", observedProbeMarkers)}]";
        return passed;
    }
}
