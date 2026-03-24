using System.Collections.Immutable;
using System.Text.Json.Nodes;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.E2ETests.Assertions;

public class PostgreSqlGeometryRoundTripAssertion : BaseAssertion<object>
{
    public override bool Assert(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        var session = sessionDataList.SingleOrDefault();
        var publishedPayload = session?.Inputs?
            .SelectMany(input => input.Data)
            .Select(data => data.Body)
            .OfType<PostgreSqlGeometryPayload>()
            .SingleOrDefault();
        var publishedId = publishedPayload?.Id;
        var publishedName = publishedPayload?.Name;
        var consumedRows = session?.Outputs?
            .SelectMany(output => output.Data)
            .Select(data => data.Body)
            .OfType<JsonObject>()
            .ToList() ?? [];

        var matchedRow = consumedRows.SingleOrDefault(row =>
            row["id"]?.GetValue<int>() == publishedId &&
            row["name"]?.GetValue<string>() == publishedName);
        var geometryValue = matchedRow?["shape"]?.GetValue<string>();
        var passed = publishedPayload != null &&
                     matchedRow != null &&
                     !string.IsNullOrWhiteSpace(geometryValue);

        AssertionMessage = passed
            ? "PostgreSQL round-trip completed successfully and the geometry column was consumed as a string."
            : "Expected the PostgreSQL consumer to read the published row and materialize the geometry column as string.";
        AssertionTrace = publishedPayload == null
            ? $"Published payload missing. ConsumedRows={consumedRows.Count}"
            : $"PublishedId={publishedPayload.Id}, PublishedName={publishedPayload.Name}, ConsumedRows={consumedRows.Count}, GeometryLength={geometryValue?.Length ?? 0}";
        return passed;
    }
}
