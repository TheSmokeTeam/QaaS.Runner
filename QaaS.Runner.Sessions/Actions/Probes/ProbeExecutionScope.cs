using System.Diagnostics;

namespace QaaS.Runner.Sessions.Actions.Probes;

/// <summary>
/// Stores the current probe execution descriptor in <see cref="Activity.Current"/> baggage so probe loading and
/// execution can share a per-async-flow scope without depending on a sibling repository checkout in CI.
/// </summary>
internal static class ProbeExecutionScope
{
    internal const string SessionNameBaggageKey = "qaas.probe.session-name";
    internal const string ProbeNameBaggageKey = "qaas.probe.probe-name";

    internal static IDisposable Enter(string sessionName, string probeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(probeName);

        var activity = new Activity("QaaS.ProbeExecutionScope");
        activity.AddBaggage(SessionNameBaggageKey, sessionName);
        activity.AddBaggage(ProbeNameBaggageKey, probeName);
        activity.Start();

        return new ActivityScope(activity);
    }

    internal static bool TryGetCurrent(out (string SessionName, string ProbeName) descriptor)
    {
        var currentActivity = Activity.Current;
        var sessionName = currentActivity?.GetBaggageItem(SessionNameBaggageKey);
        var probeName = currentActivity?.GetBaggageItem(ProbeNameBaggageKey);
        if (string.IsNullOrWhiteSpace(sessionName) || string.IsNullOrWhiteSpace(probeName))
        {
            descriptor = default;
            return false;
        }

        descriptor = (sessionName, probeName);
        return true;
    }

    internal static (string SessionName, string ProbeName) GetCurrent()
    {
        if (TryGetCurrent(out var descriptor))
        {
            return descriptor;
        }

        throw new InvalidOperationException(
            "Probe execution scope is not available. Runner should wrap probe configuration loading and execution " +
            "inside ProbeExecutionScope.Enter so global-dictionary probe paths stay unique per session and probe.");
    }

    private sealed class ActivityScope(Activity activity) : IDisposable
    {
        public void Dispose()
        {
            activity.Stop();
        }
    }
}
