using System.Linq;
using NUnit.Framework;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;
using QaaS.Runner.Assertions.Reporters;
using QaaS.Runner.Assertions.Reporters.ReportPortal;

namespace QaaS.Runner.Assertions.Tests;

[TestFixture]
public class ReportPortalSessionArtifactTests
{
    [Test]
    public void BuildSessionArtifact_WithRepeatedS3Failures_SerializesNormalizedClone()
    {
        var firstFailure = CreateS3Failure("request-z", "host-z");
        var secondFailure = CreateS3Failure("request-a", "host-a");
        var sessionData = new SessionData
        {
            Name = "s3-session",
            SessionFailures = [firstFailure, secondFailure],
        };
        var reporter = new ExposedReportPortalReporter
        {
            Config = new ReportPortalConfig
            {
                Enabled = true,
                Endpoint = "https://reportportal.local/api/",
                ApiKey = "api-key",
            },
            SaveSessionData = true,
        };

        var artifact = reporter.BuildSessionArtifactForTest(
            sessionData,
            new Assertion { SaveSessionData = false }
        );

        var serializedSessionData = SessionDataSerialization.DeserializeSessionData(
            artifact!.Content
        );
        var serializedFailure = serializedSessionData.SessionFailures.Single();
        Assert.Multiple(() =>
        {
            Assert.That(artifact, Is.Not.Null);
            Assert.That(serializedSessionData.SessionFailures, Has.Count.EqualTo(1));
            Assert.That(
                serializedFailure.Reason.Message,
                Is.EqualTo(
                    "S3 operation failed: StatusCode=404 NotFound, ErrorCode=NoSuchKey, Message=missing object"
                )
            );
            Assert.That(serializedFailure.Reason.Description, Does.Contain("request-a"));
            Assert.That(serializedFailure.Reason.Description, Does.Contain("host-a"));
            Assert.That(
                sessionData.SessionFailures,
                Is.EqualTo(new[] { firstFailure, secondFailure })
            );
        });
    }

    private static ActionFailure CreateS3Failure(string requestId, string amazonId2) =>
        new()
        {
            Name = "S3Consumer",
            ActionType = "ChunkConsumer",
            Reason = new Reason
            {
                Message =
                    $"S3 operation failed: StatusCode=404 NotFound, ErrorCode=NoSuchKey, RequestId={requestId}, AmazonId2={amazonId2}, Message=missing object",
                Description = $"RequestId: {requestId}\nAmazonId2: {amazonId2}",
            },
        };

    private sealed class ExposedReportPortalReporter : ReportPortalReporter
    {
        public ReportArtifact? BuildSessionArtifactForTest(
            SessionData sessionData,
            Assertion assertion
        ) => base.BuildSessionArtifact(sessionData, assertion);
    }
}
