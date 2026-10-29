using Microsoft.Extensions.Logging;

namespace QaaS.Runner;

internal static class RunnerDiagnosticEvents
{
    public static readonly EventId PhaseFailed = new(4101, nameof(PhaseFailed));
    public static readonly EventId ConfigurationRejected = new(4102, nameof(ConfigurationRejected));
}
