using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Runner.Sessions.Actions.Probes;
using QaaS.Runner.Tests.TestObjects;

namespace QaaS.Runner.Tests.Extensions;

[TestFixture]
public class ProbeExecutionContextTests
{
    [SetUp]
    public void SetUp()
    {
        ProbeRunRecorder.Reset();
    }

    [Test]
    public void Act_ShouldExposeCurrentProbeScopeToProbeHook()
    {
        var probeHook = new ScopeAwareTestProbe
        {
            Context = new InternalContext
            {
                Logger = Globals.Logger,
                ExecutionId = "execution-a",
                CaseName = "case-a",
                InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>())
            },
            Configuration = new ProbeMarkerConfig { Marker = "ignored" }
        };

        var probe = new Probe(
            "restore-queues",
            "recovery-session",
            0,
            probeHook,
            [],
            [],
            NullLogger.Instance);

        probe.Act();

        Assert.That(ProbeRunRecorder.GetScopedRuns(), Is.EqualTo(new[]
        {
            ("recovery-session", "restore-queues")
        }));
        Assert.That(ProbeExecutionContext.TryGetCurrent(probeHook.Context, out _), Is.False);
    }
}
