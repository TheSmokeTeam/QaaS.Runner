using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
    public async Task PublishAsync_KeepsContextAndMetadataOnlyInItemDetails()
    {
        var factory = new RecordingClientFactory();
        using var publisher = CreateSuccessfulPublisher(factory, out _);
        var reporter = CreateReporter(
            executionId: "execution-1",
            caseName: "case-a",
            extraLabels: new Dictionary<string, string> { ["Area"] = "Checkout" });
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
            Assert.That(itemRequest.Parameters,
                Does.Contain(new KeyValuePair<string, string>("Project", "Smoke")));
            Assert.That(itemRequest.Attributes.Any(attribute =>
                attribute.Key == "Area" && attribute.Value == "Checkout"), Is.True);
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
        IReadOnlyDictionary<string, string>? extraLabels = null)
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
            Config = new ReportPortalConfig
            {
                Enabled = enabled,
                Endpoint = endpoint,
                ApiKey = apiKey,
                Project = project
            },
            Context = context,
            ExecutionMode = "run",
            EpochTestSuiteStartTime = new DateTimeOffset(
                new DateTime(2025, 1, 1, 9, 30, 0, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
        };
    }

    private static AssertionResult CreateResult(string assertionName, string sessionName, long testDurationMs = 1)
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
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] }
        };
    }

    private sealed class RecordingClientFactory
    {
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
                ThrowOnDispose);
            Services.Add(service);
            return service.Object;
        }
    }

    private sealed class RecordingReportPortalPublisher(
        RecordingHttpMessageHandler validationHandler,
        RecordingClientFactory clientFactory,
        ILogger logger) : ReportPortalPublisher(logger)
    {
        protected override Task<HttpResponseMessage> SendValidationRequestAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return validationHandler.SendAsyncForTest(request, cancellationToken);
        }

        protected override IClientService CreateClient(Uri endpointUri, string project, string apiKey)
        {
            return clientFactory.Create();
        }
    }

    private sealed class RecordingClientService
    {
        private readonly Mock<IClientService> _service = new();
        private readonly Mock<ILaunchResource> _launchResource = new();
        private readonly Mock<ITestItemResource> _testItemResource = new();
        private readonly Mock<ILogItemResource> _logItemResource = new();

        public RecordingClientService(bool throwOnLaunchStart, bool throwOnLaunchFinish,
            bool throwOnTestItemStart, bool throwOnDispose)
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

                    return new LaunchFinishedResponse { Uuid = "launch" };
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
}
