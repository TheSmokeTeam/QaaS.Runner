using System.Collections.Immutable;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Generator;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.MetaDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.E2ETests.Generators.ReportPortal;

/// <summary>
/// Emits deterministic payloads that make ReportPortal item attachments and traces easy to validate across repeated
/// E2E runs.
/// </summary>
public sealed class ReportPortalPayloadGenerator : BaseGenerator<ReportPortalPayloadGeneratorConfig>
{
    public override IEnumerable<Data<object>> Generate(IImmutableList<SessionData> sessionDataList,
        IImmutableList<DataSource> dataSourceList)
    {
        for (var itemIndex = 0; itemIndex < Configuration.Count; itemIndex++)
        {
            var payloadId = $"{Configuration.PayloadPrefix}-{itemIndex + 1}";
            var evidence = Enumerable.Range(1, Configuration.EvidenceCount)
                .Select(evidenceIndex => new ReportPortalEvidenceEntry
                {
                    Title = $"{payloadId}-evidence-{evidenceIndex}",
                    Category = evidenceIndex % 2 == 0 ? "diagnostic" : "observability",
                    Status = evidenceIndex % 3 == 0 ? "warning" : "info",
                    Hint =
                        $"{Configuration.Component}/{Configuration.Area}/{Configuration.Capability}/evidence-{evidenceIndex}"
                })
                .ToArray();
            yield return new Data<object>
            {
                Body = new ReportPortalGeneratedPayload
                {
                    Index = itemIndex,
                    PayloadId = payloadId,
                    Component = Configuration.Component,
                    Area = Configuration.Area,
                    Owner = Configuration.Owner,
                    EnvironmentName = Configuration.EnvironmentName,
                    Capability = Configuration.Capability,
                    Region = Configuration.Region,
                    Scenario = Configuration.Scenario,
                    TraceId = $"{Configuration.PayloadPrefix}-trace-{itemIndex + 1}",
                    CorrelationId = $"{Configuration.PayloadPrefix}-corr-{itemIndex + 1}",
                    Tags = Configuration.Tags
                        .Concat([
                            Configuration.Component,
                            Configuration.Area,
                            Configuration.Owner,
                            Configuration.EnvironmentName,
                            Configuration.Capability
                        ])
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Component"] = Configuration.Component,
                        ["Area"] = Configuration.Area,
                        ["Owner"] = Configuration.Owner,
                        ["Environment"] = Configuration.EnvironmentName,
                        ["Capability"] = Configuration.Capability,
                        ["Scenario"] = Configuration.Scenario,
                        ["Region"] = Configuration.Region
                    },
                    Metrics = new Dictionary<string, double>
                    {
                        ["durationMs"] = 125 + (itemIndex * 17),
                        ["recordsProcessed"] = 10 + (itemIndex * 3),
                        ["assertionCoverage"] = 0.75 + (itemIndex * 0.03)
                    },
                    Evidence = evidence,
                    Notes = Configuration.Notes
                        .Concat([
                            $"Payload {payloadId} prepared for {Configuration.Scenario}.",
                            $"Environment={Configuration.EnvironmentName}; Capability={Configuration.Capability}; Region={Configuration.Region}."
                        ])
                        .ToArray()
                },
                MetaData = new MetaData
                {
                    RabbitMq = new RabbitMq
                    {
                        RoutingKey =
                            $"{Configuration.Component.ToLowerInvariant()}.{Configuration.Area.ToLowerInvariant()}.{itemIndex + 1}"
                    }
                }
            };
        }
    }
}
