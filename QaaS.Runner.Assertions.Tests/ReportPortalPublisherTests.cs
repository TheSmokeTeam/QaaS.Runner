using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using QaaS.Framework.Configurations.CustomExceptions;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;
using QaaS.Runner.Assertions.Reporters.ReportPortal;
using QaaS.Runner.Assertions.Tests.Mocks;
using ReportPortal.Client.Abstractions;
using ReportPortal.Client.Abstractions.Requests;
using ReportPortal.Client.Abstractions.Resources;
using ReportPortal.Client.Abstractions.Responses;

namespace QaaS.Runner.Assertions.Tests;

[TestFixture]
public class ReportPortalPublisherTests
{
    [SetUp]
    public void SetUp()
    {
        ReportPortalConfig.RegisterDefaults(enabled: false);
    }

    [Test]
    public async Task ValidateAsync_WithDisabledReportPortal_DoesNotValidateOrCreatePublishClient()
    {
        var factory = new RecordingClientFactory();
        using var publisher = CreateSuccessfulPublisher(factory, out var handler);
        var reporter = CreateReporter(enabled: false);

        await publisher.ValidateAsync([reporter]);

        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestCount, Is.Zero);
            Assert.That(factory.Services, Is.Empty);
        });
    }

    [TestCase("ReportPortal.ApiKey", null, "http://localhost:8080", TestName = "Missing API key")]
    [TestCase("ReportPortal.Endpoint", "api-key", null, TestName = "Missing endpoint")]
    public void ValidateAsync_WithMissingConfiguration_ThrowsBeforeHttpOrPublishClient(
        string expectedMessage,
        string? apiKey,
        string? endpoint)
    {
        var factory = new RecordingClientFactory();
        using var publisher = CreateSuccessfulPublisher(factory, out var handler);
        var reporter = CreateReporter(apiKey: apiKey, endpoint: endpoint);

        var exception = Assert.ThrowsAsync<InvalidConfigurationsException>(
            async () => await publisher.ValidateAsync([reporter]));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain(expectedMessage));
            Assert.That(handler.RequestCount, Is.Zero);
            Assert.That(factory.Services, Is.Empty);
        });
    }

    [Test]
    public void ValidateAsync_WithUnauthorizedApiKey_ThrowsConfigurationFailure()
    {
        var factory = new RecordingClientFactory();
        var logger = new Mock<ILogger>();
        using var publisher = CreatePublisher(factory, _ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("unauthorized", Encoding.UTF8, "text/plain")
            }, out var handler, logger.Object);

        var exception = Assert.ThrowsAsync<InvalidConfigurationsException>(
            async () => await publisher.ValidateAsync([CreateReporter()]));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("API key"));
            Assert.That(handler.RequestCount, Is.EqualTo(1));
            Assert.That(factory.Services, Is.Empty);
        });
        VerifyErrorLogged(logger, "configured API key was rejected");
    }

    [Test]
    public void ValidateAsync_WhenEndpointIsUnreachable_ThrowsConfigurationFailureAndLogsError()
    {
        var factory = new RecordingClientFactory();
        var logger = new Mock<ILogger>();
        using var publisher = CreatePublisher(factory, _ => throw new HttpRequestException("Connection refused"),
            out var handler, logger.Object);

        var exception = Assert.ThrowsAsync<InvalidConfigurationsException>(
            async () => await publisher.ValidateAsync([CreateReporter()]));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("endpoint `http://localhost:8080` is unreachable"));
            Assert.That(handler.RequestCount, Is.EqualTo(1));
            Assert.That(factory.Services, Is.Empty);
        });
        VerifyErrorLogged(logger, "endpoint `http://localhost:8080` is unreachable");
    }

    [Test]
    public void ValidateAsync_WithMissingProject_ThrowsConfigurationFailure()
    {
        var factory = new RecordingClientFactory();
        using var publisher = CreatePublisher(factory, _ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"message\":\"not found\"}", Encoding.UTF8,
                    "application/json")
            }, out var handler);

        var exception = Assert.ThrowsAsync<InvalidConfigurationsException>(
            async () => await publisher.ValidateAsync([CreateReporter(team: "Smoke")]));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("no accessible project matches `Smoke`"));
            Assert.That(handler.RequestCount, Is.EqualTo(1));
            Assert.That(factory.Services, Is.Empty);
        });
    }

    [Test]
    public async Task ValidateAsync_WithSuccessfulProjectLookup_UsesProjectFallbackAndCreatesNoPublishClient()
    {
        var factory = new RecordingClientFactory();
        using var publisher = CreateSuccessfulPublisher(factory, out var handler);

        await publisher.ValidateAsync([CreateReporter(team: "Smoke", project: null)]);

        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestCount, Is.EqualTo(1));
            Assert.That(handler.RequestedProjectNames, Is.EqualTo(new[] { "Smoke" }));
            Assert.That(factory.Services, Is.Empty);
        });
    }

    [Test]
    public async Task ValidateAsync_GroupsSuccessfulValidationByNormalizedLaunchGroup()
    {
        var factory = new RecordingClientFactory();
        using var publisher = CreateSuccessfulPublisher(factory, out var handler);
        var firstReporter = CreateReporter(endpoint: "http://localhost:8080");
        var equivalentEndpointReporter = CreateReporter(endpoint: "http://localhost:8080/api/v1/");

        await publisher.ValidateAsync([firstReporter, equivalentEndpointReporter]);

        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestCount, Is.EqualTo(1));
            Assert.That(factory.Services, Is.Empty);
        });
    }

    [Test]
    public async Task ValidateAsync_WithDifferentSystem_ValidatesSeparateLaunchGroup()
    {
        var factory = new RecordingClientFactory();
        using var publisher = CreateSuccessfulPublisher(factory, out var handler);

        await publisher.ValidateAsync([
            CreateReporter(system: "QaaS"),
            CreateReporter(system: "AnotherSystem")
        ]);

        Assert.That(handler.RequestCount, Is.EqualTo(2));
    }

    [Test]
    public async Task PublishAsync_WithSameNormalizedEndpointProjectAndSystem_PublishesOneLaunch()
    {
        var factory = new RecordingClientFactory();
        using var publisher = CreateSuccessfulPublisher(factory, out var handler);
        var firstReporter = CreateReporter(team: "Smoke", system: "QaaS", endpoint: "http://localhost:8080");
        var secondReporter = CreateReporter(team: "Smoke", system: "QaaS", endpoint: "http://localhost:8080/api/v1/");
        firstReporter.WriteTestResults(CreateResult("assertion-a", "Session A"));
        secondReporter.WriteTestResults(CreateResult("assertion-b", "Session B"));

        await publisher.ValidateAsync([firstReporter, secondReporter]);
        await publisher.PublishAsync([firstReporter, secondReporter]);

        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestCount, Is.EqualTo(1));
            Assert.That(factory.Services, Has.Count.EqualTo(1));
            Assert.That(factory.Services[0].LaunchStartRequests, Has.Count.EqualTo(1));
            Assert.That(factory.Services[0].TestItemStartRequests, Has.Count.EqualTo(2));
        });
    }

    [TestCase(true, 1)]
    [TestCase(false, 0)]
    [TestCase(null, 1)]
    public async Task PublishAsync_WithTerminalOutput_RespectsConfiguration(bool? enabled, int expected)
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "stdout\nstderr");
        try
        {
            var factory = new RecordingClientFactory();
            using var publisher = CreateSuccessfulPublisher(factory, out _);
            publisher.TerminalOutputPath = path;
            var reporter = CreateReporter(saveTerminalOutput: enabled);
            reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));
            await publisher.ValidateAsync([reporter]);
            await publisher.PublishAsync([reporter]);
            Assert.That(factory.Services.Single().LogItemRequests
                .Count(request => request.Attach?.Name == "terminal.log"), Is.EqualTo(expected));
            if (enabled != false) Assert.That(factory.Services.Single().LogItemRequests.Single(request => request.Attach?.Name == "terminal.log").Attach!.Data, Is.EqualTo(Encoding.UTF8.GetBytes("stdout\nstderr")));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task PublishAsync_WithLongAssertionDuration_LaunchContainsAssertionTiming()
    {
        var factory = new RecordingClientFactory();
        using var publisher = CreateSuccessfulPublisher(factory, out _);
        var reporter = CreateReporter();
        reporter.WriteTestResults(CreateResult("long-assertion", "Session A",
            testDurationMs: (long)TimeSpan.FromMinutes(30).TotalMilliseconds));

        await publisher.ValidateAsync([reporter]);
        await publisher.PublishAsync([reporter]);

        var service = factory.Services.Single();
        var launchStartTime = service.LaunchStartRequests.Single().StartTime;
        var launchEndTime = service.LaunchFinishRequests.Single().EndTime;
        var assertionStartTime = service.TestItemStartRequests.Single().StartTime;
        var assertionEndTime = service.TestItemFinishRequests.Single().EndTime;
        var expectedStartTime = new DateTime(2025, 1, 1, 9, 30, 0, DateTimeKind.Utc);

        Assert.Multiple(() =>
        {
            Assert.That(launchStartTime, Is.EqualTo(expectedStartTime));
            Assert.That(launchStartTime, Is.EqualTo(assertionStartTime));
            Assert.That(launchEndTime, Is.EqualTo(assertionEndTime));
            Assert.That(launchEndTime - launchStartTime,
                Is.EqualTo(TimeSpan.FromMinutes(30)));
        });
    }

    [Test]
    public async Task PublishAsync_KeepsLogTimesInsideAssertionWindow()
    {
        var factory = new RecordingClientFactory();
        using var publisher = CreateSuccessfulPublisher(factory, out _);
        var reporter = CreateReporter();
        reporter.SaveTemplate = true;
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A", testDurationMs: 5_000));

        await publisher.ValidateAsync([reporter]);
        await publisher.PublishAsync([reporter]);

        var service = factory.Services.Single();
        var assertionStartTime = service.TestItemStartRequests.Single().StartTime;
        var assertionEndTime = service.TestItemFinishRequests.Single().EndTime;
        var templateAttachment = service.LogItemRequests
            .Single(request => request.Attach?.Name == "template.yaml").Attach!;

        Assert.Multiple(() =>
        {
            Assert.That(templateAttachment.MimeType, Is.EqualTo("text/plain"));
            Assert.That(service.LogItemRequests.All(request =>
                request.Time >= assertionStartTime && request.Time <= assertionEndTime), Is.True);
        });
    }

    [Test]
    public async Task PublishAsync_WhenLaunchFinishes_LogsReportLink()
    {
        const string reportLink = "http://localhost:8080/ui/#Smoke/launches/all/42";
        var factory = new RecordingClientFactory { LaunchLink = reportLink };
        var logger = new Mock<ILogger>();
        using var publisher = CreateSuccessfulPublisher(factory, out _, logger.Object);
        var reporter = CreateReporter();
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));

        await publisher.ValidateAsync([reporter]);
        await publisher.PublishAsync([reporter]);

        VerifyInformationLoggedExactly(logger, $"ReportPortal report: {reportLink}");
        Assert.That(publisher.OpenedReportLinks, Is.Empty);
    }

    [TestCase("http://localhost:8080/ui/#Smoke/launches/all/42", true)]
    [TestCase("https://reportportal.example/ui/#Smoke/launches/all/42", true)]
    [TestCase("javascript:alert('unsafe')", false)]
    public async Task PublishAsync_WhenOpenReportPortalIsEnabled_OpensOnlyWebReportLinks(
        string reportLink,
        bool shouldOpen)
    {
        var factory = new RecordingClientFactory { LaunchLink = reportLink };
        using var publisher = CreateSuccessfulPublisher(factory, out _);
        publisher.OpenReportPortal = true;
        var reporter = CreateReporter();
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));

        await publisher.ValidateAsync([reporter]);
        await publisher.PublishAsync([reporter]);

        var expectedLinks = shouldOpen ? new[] { reportLink } : [];
        Assert.That(publisher.OpenedReportLinks, Is.EqualTo(expectedLinks));
    }

    [Test]
    public async Task PublishAsync_AddsExecutionIdsToLaunchAndExecutionIdToItemDetails()
    {
        var factory = new RecordingClientFactory();
        using var publisher = CreateSuccessfulPublisher(factory, out _);
        var reporter = CreateReporter(
            executionId: "execution-1",
            caseName: "case-a",
            extraLabels: new Dictionary<string, string> { ["Area"] = "Checkout" },
            reportPortalAttributes: new Dictionary<string, string> { ["LaunchOnly"] = "value" });
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));

        await publisher.ValidateAsync([reporter]);
        await publisher.PublishAsync([reporter]);

        var service = factory.Services.Single();
        var itemRequest = service.TestItemStartRequests.Single();

        Assert.Multiple(() =>
        {
            Assert.That(itemRequest.Parameters,
                Does.Contain(new KeyValuePair<string, string>("Execution Id", "execution-1")));
            Assert.That(itemRequest.Parameters,
                Does.Contain(new KeyValuePair<string, string>("Case Name", "case-a")));
            Assert.That(itemRequest.Parameters,
                Does.Contain(new KeyValuePair<string, string>("Area", "Checkout")));
            Assert.That(itemRequest.Parameters.Any(parameter => parameter.Key == "Project"), Is.False);
            Assert.That(itemRequest.Attributes.Any(attribute =>
                attribute.Key == "Area" && attribute.Value == "Checkout"), Is.True);
            Assert.That(itemRequest.Attributes
                .Where(attribute => attribute.Key == "executionId")
                .Select(attribute => attribute.Value), Is.EqualTo(new[] { "execution-1" }));
            Assert.That(itemRequest.Attributes
                .Where(attribute => attribute.Key == "caseName")
                .Select(attribute => attribute.Value), Is.EqualTo(new[] { "case-a" }));
            Assert.That(itemRequest.Attributes.Any(attribute => attribute.Key == "executionMode"), Is.False);
            Assert.That(itemRequest.Attributes.Any(attribute => attribute.Key == "builderCount"), Is.False);
            Assert.That(itemRequest.Attributes.Single(attribute =>
                attribute.Key == "sessionCount").Value, Is.EqualTo("1"));
            Assert.That(itemRequest.Attributes.Any(attribute => attribute.Key == "caseNames"), Is.False);
            Assert.That(itemRequest.Attributes.Any(attribute => attribute.Key == "environment"), Is.False);
            Assert.That(itemRequest.Attributes.Any(attribute => attribute.Key == "LaunchOnly"), Is.False);
            Assert.That(service.LaunchStartRequests.Single().Attributes.Any(attribute =>
                attribute.Key == "LaunchOnly" && attribute.Value == "value"), Is.True);
            Assert.That(service.LaunchStartRequests.Single().Attributes.Any(attribute =>
                attribute.Key == "executionIds" && attribute.Value == "execution-1"), Is.True);
            Assert.That(itemRequest.Description, Does.Not.Contain("Execution context:"));
            Assert.That(itemRequest.Description, Does.Not.Contain("Metadata attributes:"));
            Assert.That(itemRequest.Description, Does.Not.Contain("execution-1"));
            Assert.That(itemRequest.Description, Does.Not.Contain("case-a"));
            Assert.That(itemRequest.Description, Does.Not.Contain("Checkout"));
            Assert.That(itemRequest.Attributes.Any(attribute => attribute.Key == "tool"), Is.False);
            Assert.That(itemRequest.Attributes.Any(attribute => attribute.Key == "source"), Is.False);
            Assert.That(itemRequest.Attributes.Any(attribute => attribute.Key == "project"), Is.False);
            Assert.That(service.LaunchStartRequests.Single().Attributes.Any(attribute =>
                attribute.Key is "tool" or "source" or "project"), Is.False);
            Assert.That(service.LogItemRequests.Any(request =>
                request.Text.Contains("Assertion context:", StringComparison.Ordinal)), Is.False);
            Assert.That(service.LogItemRequests.Any(request =>
                request.Attach?.Name == "assertion-context.json"), Is.False);
        });
    }

    [Test]
    public async Task PublishAsync_WithAssertionMessage_AddsBlankLineBeforeAssertionConfiguration()
    {
        var factory = new RecordingClientFactory();
        using var publisher = CreateSuccessfulPublisher(factory, out _);
        var reporter = CreateReporter();
        var result = CreateResult("assertion-a", "Session A");
        result.Assertion.AssertionHook = new AssertionHookMock
        {
            AssertionMessage = "assertion-a-message"
        };
        reporter.WriteTestResults(result);

        await publisher.ValidateAsync([reporter]);
        await publisher.PublishAsync([reporter]);

        var description = factory.Services.Single().TestItemStartRequests.Single().Description;

        Assert.That(description, Does.Contain(
            $"assertion-a-message{Environment.NewLine}<br />{Environment.NewLine}Assertion configuration:"));
    }

    [Test]
    public async Task PublishAsync_WithoutAssertionMessage_AddsVisibleLineBreakBeforeAssertionConfiguration()
    {
        var factory = new RecordingClientFactory();
        using var publisher = CreateSuccessfulPublisher(factory, out _);
        var reporter = CreateReporter();
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));

        await publisher.ValidateAsync([reporter]);
        await publisher.PublishAsync([reporter]);

        var description = factory.Services.Single().TestItemStartRequests.Single().Description;

        Assert.That(description, Does.StartWith(
            $"<br />{Environment.NewLine}Assertion configuration:"));
    }

    [Test]
    public async Task PublishAsync_AddsAssertionScopedSessionNamesAndSessionCount()
    {
        var factory = new RecordingClientFactory();
        using var publisher = CreateSuccessfulPublisher(factory, out _);
        var reporter = CreateReporter();
        var firstResult = CreateResult("assertion-a", "Session B");
        firstResult.Assertion.SessionDataList = firstResult.Assertion.SessionDataList.Add(new SessionData
        {
            Name = "Session A",
            UtcStartTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            UtcEndTime = new DateTime(2025, 1, 1, 10, 0, 1, DateTimeKind.Utc)
        });
        reporter.WriteTestResults(firstResult);
        reporter.WriteTestResults(CreateResult("assertion-b", "Session C"));

        await publisher.ValidateAsync([reporter]);
        await publisher.PublishAsync([reporter]);

        var service = factory.Services.Single();
        var items = service.TestItemStartRequests.ToDictionary(request => request.Name);

        Assert.Multiple(() =>
        {
            Assert.That(items["assertion-a"].Attributes.Single(attribute =>
                attribute.Key == "sessionNames").Value, Is.EqualTo("Session A, Session B"));
            Assert.That(items["assertion-b"].Attributes.Single(attribute =>
                attribute.Key == "sessionNames").Value, Is.EqualTo("Session C"));
            Assert.That(items.Values.SelectMany(item => item.Attributes).Any(attribute =>
                attribute.Key == "sessions"), Is.False);
            Assert.That(items.Values.SelectMany(item => item.Attributes).Any(attribute =>
                attribute.Key == "session"), Is.False);
            Assert.That(items["assertion-a"].Attributes.Single(attribute =>
                attribute.Key == "sessionCount").Value, Is.EqualTo("2"));
            Assert.That(items["assertion-b"].Attributes.Single(attribute =>
                attribute.Key == "sessionCount").Value, Is.EqualTo("1"));
            Assert.That(service.LaunchStartRequests.Single().Attributes.Single(attribute =>
                attribute.Key == "sessionCount").Value, Is.EqualTo("3"));
        });
    }

    [Test]
    public async Task PublishAsync_AddsAssertionScopedFlakyState()
    {
        var factory = new RecordingClientFactory();
        using var publisher = CreateSuccessfulPublisher(factory, out _);
        var reporter = CreateReporter();
        reporter.WriteTestResults(CreateResult("flaky-assertion", "Session A", isFlaky: true));
        reporter.WriteTestResults(CreateResult("stable-assertion", "Session B"));

        await publisher.ValidateAsync([reporter]);
        await publisher.PublishAsync([reporter]);

        var items = factory.Services.Single().TestItemStartRequests.ToDictionary(request => request.Name);

        Assert.Multiple(() =>
        {
            Assert.That(items["flaky-assertion"].Attributes.Single(attribute =>
                attribute.Key == "flaky").Value, Is.EqualTo("true"));
            Assert.That(items["stable-assertion"].Attributes.Single(attribute =>
                attribute.Key == "flaky").Value, Is.EqualTo("false"));
        });
    }

    [Test]
    public async Task PublishAsync_WithMixedProjectsAndSystems_PublishesSeparateLaunches()
    {
        var factory = new RecordingClientFactory();
        using var publisher = CreateSuccessfulPublisher(factory, out var handler);
        var firstReporter = CreateReporter(team: "Smoke", system: "QaaS");
        var secondReporter = CreateReporter(team: "AnotherTeam", system: "QaaS");
        var thirdReporter = CreateReporter(team: "Smoke", system: "AnotherSystem");
        firstReporter.WriteTestResults(CreateResult("assertion-a", "Session A"));
        secondReporter.WriteTestResults(CreateResult("assertion-b", "Session B"));
        thirdReporter.WriteTestResults(CreateResult("assertion-c", "Session C"));

        await publisher.ValidateAsync([firstReporter, secondReporter, thirdReporter]);
        await publisher.PublishAsync([firstReporter, secondReporter, thirdReporter]);

        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestCount, Is.EqualTo(3));
            Assert.That(factory.Services, Has.Count.EqualTo(3));
            Assert.That(factory.Services.Sum(service => service.LaunchStartRequests.Count), Is.EqualTo(3));
        });
    }

    [Test]
    public async Task PublishAsync_WithNoQueuedResults_DoesNotCreatePublishClient()
    {
        var factory = new RecordingClientFactory();
        using var publisher = CreateSuccessfulPublisher(factory, out _);
        var reporter = CreateReporter();

        await publisher.ValidateAsync([reporter]);
        await publisher.PublishAsync([reporter]);

        Assert.That(factory.Services, Is.Empty);
    }

    [Test]
    public async Task PublishAsync_WhenLaunchStartFails_DoesNotThrowAndLogsError()
    {
        var factory = new RecordingClientFactory { ThrowOnLaunchStart = true };
        var logger = new Mock<ILogger>();
        using var publisher = CreateSuccessfulPublisher(factory, out _, logger.Object);
        var reporter = CreateReporter();
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));

        await publisher.ValidateAsync([reporter]);

        Assert.DoesNotThrowAsync(async () => await publisher.PublishAsync([reporter]));
        VerifyErrorLogged(logger, "Could not publish ReportPortal launch");
    }

    [Test]
    public async Task PublishAsync_WhenLaunchFinishFails_DoesNotThrowAndLogsError()
    {
        var factory = new RecordingClientFactory { ThrowOnLaunchFinish = true };
        var logger = new Mock<ILogger>();
        using var publisher = CreateSuccessfulPublisher(factory, out _, logger.Object);
        var reporter = CreateReporter();
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));

        await publisher.ValidateAsync([reporter]);

        Assert.DoesNotThrowAsync(async () => await publisher.PublishAsync([reporter]));
        VerifyErrorLogged(logger, "Could not finish ReportPortal launch");
    }

    [Test]
    public async Task PublishAsync_WhenItemPublishFails_DoesNotThrowAndLogsError()
    {
        var factory = new RecordingClientFactory { ThrowOnTestItemStart = true };
        var logger = new Mock<ILogger>();
        using var publisher = CreateSuccessfulPublisher(factory, out _, logger.Object);
        var reporter = CreateReporter();
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));

        await publisher.ValidateAsync([reporter]);

        Assert.DoesNotThrowAsync(async () => await publisher.PublishAsync([reporter]));
        VerifyErrorLogged(logger, "Could not publish assertion");
    }

    [Test]
    public async Task PublishAsync_DisposesClientServiceAfterPublishing()
    {
        var factory = new RecordingClientFactory();
        using var publisher = CreateSuccessfulPublisher(factory, out _);
        var reporter = CreateReporter();
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));

        await publisher.ValidateAsync([reporter]);
        Assert.That(factory.Services, Is.Empty);

        await publisher.PublishAsync([reporter]);

        Assert.That(factory.Services.Single().DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task PublishAsync_WhenClientDisposeFails_DoesNotThrowAndLogsError()
    {
        var factory = new RecordingClientFactory { ThrowOnDispose = true };
        var logger = new Mock<ILogger>();
        using var publisher = CreateSuccessfulPublisher(factory, out _, logger.Object);
        var reporter = CreateReporter();
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));

        await publisher.ValidateAsync([reporter]);

        Assert.DoesNotThrowAsync(async () => await publisher.PublishAsync([reporter]));
        VerifyErrorLogged(logger, "Could not dispose ReportPortal client service");
    }

    private static RecordingReportPortalPublisher CreateSuccessfulPublisher(RecordingClientFactory factory,
        out RecordingHttpMessageHandler handler)
    {
        return CreateSuccessfulPublisher(factory, out handler, Globals.Logger);
    }

    private static RecordingReportPortalPublisher CreateSuccessfulPublisher(RecordingClientFactory factory,
        out RecordingHttpMessageHandler handler,
        ILogger logger)
    {
        return CreatePublisher(factory, request =>
        {
            var projectName = request.RequestUri!.Segments[^1];
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"projectName\":\"{projectName}\"}}", Encoding.UTF8,
                    "application/json")
            };
        }, out handler, logger);
    }

    private static RecordingReportPortalPublisher CreatePublisher(RecordingClientFactory factory,
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory,
        out RecordingHttpMessageHandler handler)
    {
        return CreatePublisher(factory, responseFactory, out handler, Globals.Logger);
    }

    private static RecordingReportPortalPublisher CreatePublisher(RecordingClientFactory factory,
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory,
        out RecordingHttpMessageHandler handler,
        ILogger logger)
    {
        handler = new RecordingHttpMessageHandler(responseFactory);
        return new RecordingReportPortalPublisher(handler, factory, logger);
    }

    private static ReportPortalReporter CreateReporter(
        bool enabled = true,
        string team = "Smoke",
        string system = "QaaS",
        string? project = null,
        string? endpoint = "http://localhost:8080",
        string? apiKey = "api-key",
        string? executionId = null,
        string? caseName = null,
        IReadOnlyDictionary<string, string>? extraLabels = null,
        IReadOnlyDictionary<string, string>? reportPortalAttributes = null,
        bool? saveTerminalOutput = true)
    {
        var context = new InternalContext
        {
            Logger = Globals.Logger,
            RootConfiguration = new ConfigurationBuilder().Build(),
            ExecutionId = executionId,
            CaseName = caseName
        };

        context.InsertValueIntoGlobalDictionary(context.GetMetaDataPath(), new MetaDataConfig
        {
            Team = team,
            System = system,
            ExtraLabels = extraLabels?.ToDictionary(
                label => label.Key,
                label => (object)label.Value,
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, object>()
        });

        return new ReportPortalReporter
        {
            SaveTerminalOutput = saveTerminalOutput,
            Config = new ReportPortalConfig
            {
                Enabled = enabled,
                Endpoint = endpoint,
                ApiKey = apiKey,
                Project = project,
                Attributes = reportPortalAttributes?.ToDictionary(attribute => attribute.Key,
                    attribute => attribute.Value)
            },
            Context = context,
            EpochTestSuiteStartTime = new DateTimeOffset(
                new DateTime(2025, 1, 1, 9, 30, 0, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
        };
    }

    private static AssertionResult CreateResult(string assertionName, string sessionName, long testDurationMs = 1,
        bool isFlaky = false)
    {
        var sessionData = new SessionData
        {
            Name = sessionName,
            UtcStartTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            UtcEndTime = new DateTime(2025, 1, 1, 10, 0, 1, DateTimeKind.Utc)
        };

        return new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = assertionName,
                AssertionName = assertionName,
                SessionDataList = ImmutableList.Create(sessionData),
                DisplayTrace = true,
                AssertionConfiguration = new ConfigurationBuilder().Build(),
                AssertionHook = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = testDurationMs,
            Flaky = new Flaky { IsFlaky = isFlaky, FlakinessReasons = [] }
        };
    }

    private sealed class RecordingClientFactory
    {
        public string LaunchLink { get; init; } = "http://localhost:8080/ui/#Smoke/launches/all/1";
        public bool ThrowOnLaunchStart { get; init; }
        public bool ThrowOnLaunchFinish { get; init; }
        public bool ThrowOnTestItemStart { get; init; }
        public bool ThrowOnDispose { get; init; }
        public List<RecordingClientService> Services { get; } = [];

        public IClientService Create()
        {
            var service = new RecordingClientService(
                ThrowOnLaunchStart,
                ThrowOnLaunchFinish,
                ThrowOnTestItemStart,
                ThrowOnDispose,
                LaunchLink);
            Services.Add(service);
            return service.Object;
        }
    }

    private sealed class RecordingReportPortalPublisher(
        RecordingHttpMessageHandler validationHandler,
        RecordingClientFactory clientFactory,
        ILogger logger) : ReportPortalPublisher(logger)
    {
        public List<string> OpenedReportLinks { get; } = [];

        protected override Task<HttpResponseMessage> SendValidationRequestAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return validationHandler.SendAsyncForTest(request, cancellationToken);
        }

        protected override IClientService CreateClient(Uri endpointUri, string project, string apiKey)
        {
            return clientFactory.Create();
        }

        protected override void OpenReportLink(string reportLink)
        {
            OpenedReportLinks.Add(reportLink);
        }
    }

    private sealed class RecordingClientService
    {
        private readonly Mock<IClientService> _service = new();
        private readonly Mock<ILaunchResource> _launchResource = new();
        private readonly Mock<ITestItemResource> _testItemResource = new();
        private readonly Mock<ILogItemResource> _logItemResource = new();

        public RecordingClientService(bool throwOnLaunchStart, bool throwOnLaunchFinish,
            bool throwOnTestItemStart, bool throwOnDispose, string launchLink)
        {
            _service.SetupGet(service => service.Launch).Returns(_launchResource.Object);
            _service.SetupGet(service => service.TestItem).Returns(_testItemResource.Object);
            _service.SetupGet(service => service.LogItem).Returns(_logItemResource.Object);
            _service.As<IDisposable>()
                .Setup(disposable => disposable.Dispose())
                .Callback(() =>
                {
                    DisposeCount++;
                    if (throwOnDispose)
                        throw new InvalidOperationException("dispose failed");
                });

            _launchResource
                .Setup(resource => resource.StartAsync(It.IsAny<StartLaunchRequest>(),
                    It.IsAny<CancellationToken>()))
                .Callback<StartLaunchRequest, CancellationToken>((request, _) => LaunchStartRequests.Add(request))
                .ReturnsAsync(() =>
                {
                    if (throwOnLaunchStart)
                        throw new InvalidOperationException("launch failed");

                    return new LaunchCreatedResponse { Uuid = $"launch-{LaunchStartRequests.Count}" };
                });
            _launchResource
                .Setup(resource => resource.FinishAsync(It.IsAny<string>(), It.IsAny<FinishLaunchRequest>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, FinishLaunchRequest, CancellationToken>((_, request, _) =>
                    LaunchFinishRequests.Add(request))
                .ReturnsAsync(() =>
                {
                    if (throwOnLaunchFinish)
                        throw new InvalidOperationException("finish failed");

                    return new LaunchFinishedResponse { Uuid = "launch", Link = launchLink };
                });

            _testItemResource
                .Setup(resource => resource.StartAsync(It.IsAny<StartTestItemRequest>(),
                    It.IsAny<CancellationToken>()))
                .Callback<StartTestItemRequest, CancellationToken>((request, _) => TestItemStartRequests.Add(request))
                .ReturnsAsync(() =>
                {
                    if (throwOnTestItemStart)
                        throw new InvalidOperationException("item failed");

                    return new TestItemCreatedResponse
                    {
                        Uuid = $"item-{TestItemStartRequests.Count}"
                    };
                });
            _testItemResource
                .Setup(resource => resource.FinishAsync(It.IsAny<string>(), It.IsAny<FinishTestItemRequest>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, FinishTestItemRequest, CancellationToken>((_, request, _) =>
                    TestItemFinishRequests.Add(request))
                .ReturnsAsync(new MessageResponse());

            _logItemResource
                .Setup(resource => resource.CreateAsync(It.IsAny<CreateLogItemRequest>(),
                    It.IsAny<CancellationToken>()))
                .Callback<CreateLogItemRequest, CancellationToken>((request, _) => LogItemRequests.Add(request))
                .ReturnsAsync(new LogItemCreatedResponse { Uuid = "log" });
        }

        public IClientService Object => _service.Object;
        public int DisposeCount { get; private set; }
        public List<StartLaunchRequest> LaunchStartRequests { get; } = [];
        public List<FinishLaunchRequest> LaunchFinishRequests { get; } = [];
        public List<StartTestItemRequest> TestItemStartRequests { get; } = [];
        public List<FinishTestItemRequest> TestItemFinishRequests { get; } = [];
        public List<CreateLogItemRequest> LogItemRequests { get; } = [];
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public List<string> RequestedProjectNames { get; } = [];

        public Task<HttpResponseMessage> SendAsyncForTest(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return SendAsync(request, cancellationToken);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestedProjectNames.Add(Uri.UnescapeDataString(request.RequestUri!.Segments[^1]));
            return Task.FromResult(responseFactory(request));
        }
    }

    private static void VerifyErrorLogged(Mock<ILogger> logger, string messageFragment)
    {
        logger.Verify(
            item => item.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) =>
                    value.ToString()!.Contains(messageFragment, StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    private static void VerifyInformationLoggedExactly(Mock<ILogger> logger, string expectedMessage)
    {
        logger.Verify(
            item => item.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) =>
                    string.Equals(value.ToString(), expectedMessage, StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
