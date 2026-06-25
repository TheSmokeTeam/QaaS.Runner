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
public class ReportPortalDeferredPublisherTests
{
    [SetUp]
    public void SetUp()
    {
        ReportPortalConfig.RegisterDefaults(enabled: false);
    }

    [Test]
    public async Task PublishAsync_WithSameNormalizedEndpointProjectAndSystem_PublishesOneLaunch()
    {
        using var validator = CreateSuccessfulValidator(out var handler);
        var factory = new RecordingClientFactory();
        using var publisher = new ReportPortalDeferredPublisher(validator, factory, StartedAt());
        var firstReporter = CreateReporter(team: "Smoke", system: "QaaS", endpoint: "http://localhost:8080");
        var secondReporter = CreateReporter(team: "Smoke", system: "QaaS", endpoint: "http://localhost:8080/api/v1/");
        firstReporter.WriteTestResults(CreateResult("assertion-a", "Session A"));
        secondReporter.WriteTestResults(CreateResult("assertion-b", "Session B"));

        await publisher.ValidateAsync([firstReporter, secondReporter], Globals.Logger);
        await publisher.PublishAsync([firstReporter, secondReporter], Globals.Logger);

        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestCount, Is.EqualTo(1));
            Assert.That(factory.Services, Has.Count.EqualTo(1));
            Assert.That(factory.Services[0].LaunchStartRequests, Has.Count.EqualTo(1));
            Assert.That(factory.Services[0].TestItemStartRequests, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task PublishAsync_WithMixedProjectsAndSystems_PublishesSeparateLaunches()
    {
        using var validator = CreateSuccessfulValidator(out var handler);
        var factory = new RecordingClientFactory();
        using var publisher = new ReportPortalDeferredPublisher(validator, factory, StartedAt());
        var firstReporter = CreateReporter(team: "Smoke", system: "QaaS");
        var secondReporter = CreateReporter(team: "AnotherTeam", system: "QaaS");
        var thirdReporter = CreateReporter(team: "Smoke", system: "AnotherSystem");
        firstReporter.WriteTestResults(CreateResult("assertion-a", "Session A"));
        secondReporter.WriteTestResults(CreateResult("assertion-b", "Session B"));
        thirdReporter.WriteTestResults(CreateResult("assertion-c", "Session C"));

        await publisher.ValidateAsync([firstReporter, secondReporter, thirdReporter], Globals.Logger);
        await publisher.PublishAsync([firstReporter, secondReporter, thirdReporter], Globals.Logger);

        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestCount, Is.EqualTo(3));
            Assert.That(factory.Services, Has.Count.EqualTo(3));
            Assert.That(factory.Services.Sum(service => service.LaunchStartRequests.Count), Is.EqualTo(3));
        });
    }

    [Test]
    public async Task PublishAsync_WithNoQueuedResults_DoesNotCreateClientService()
    {
        using var validator = CreateSuccessfulValidator(out _);
        var factory = new RecordingClientFactory();
        using var publisher = new ReportPortalDeferredPublisher(validator, factory, StartedAt());
        var reporter = CreateReporter();

        await publisher.ValidateAsync([reporter], Globals.Logger);
        await publisher.PublishAsync([reporter], Globals.Logger);

        Assert.That(factory.Services, Is.Empty);
    }

    [Test]
    public async Task PublishAsync_WhenLaunchStartFails_DoesNotThrow()
    {
        using var validator = CreateSuccessfulValidator(out _);
        var factory = new RecordingClientFactory { ThrowOnLaunchStart = true };
        using var publisher = new ReportPortalDeferredPublisher(validator, factory, StartedAt());
        var logger = new Mock<ILogger>();
        var reporter = CreateReporter();
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));

        await publisher.ValidateAsync([reporter], logger.Object);

        Assert.DoesNotThrowAsync(async () => await publisher.PublishAsync([reporter], logger.Object));
        VerifyWarningLogged(logger, "Could not publish ReportPortal launch");
    }

    [Test]
    public async Task PublishAsync_DisposesClientServiceAfterPublishing()
    {
        using var validator = CreateSuccessfulValidator(out _);
        var factory = new RecordingClientFactory();
        using var publisher = new ReportPortalDeferredPublisher(validator, factory, StartedAt());
        var reporter = CreateReporter();
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));

        await publisher.ValidateAsync([reporter], Globals.Logger);
        Assert.That(factory.Services, Is.Empty);

        await publisher.PublishAsync([reporter], Globals.Logger);

        Assert.That(factory.Services.Single().DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task PublishAsync_WhenClientDisposeFails_DoesNotThrowAndLogsWarning()
    {
        using var validator = CreateSuccessfulValidator(out _);
        var factory = new RecordingClientFactory { ThrowOnDispose = true };
        using var publisher = new ReportPortalDeferredPublisher(validator, factory, StartedAt());
        var logger = new Mock<ILogger>();
        var reporter = CreateReporter();
        reporter.WriteTestResults(CreateResult("assertion-a", "Session A"));

        await publisher.ValidateAsync([reporter], logger.Object);

        Assert.DoesNotThrowAsync(async () => await publisher.PublishAsync([reporter], logger.Object));
        VerifyWarningLogged(logger, "Could not dispose ReportPortal client service");
    }

    private static ReportPortalAccessValidator CreateSuccessfulValidator(out RecordingHttpMessageHandler handler)
    {
        handler = new RecordingHttpMessageHandler(request =>
        {
            var projectName = request.RequestUri!.Segments[^1];
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"projectName\":\"{projectName}\"}}", Encoding.UTF8,
                    "application/json")
            };
        });
        return new ReportPortalAccessValidator(new HttpClient(handler));
    }

    private static DateTimeOffset StartedAt() =>
        new(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private static ReportPortalReporter CreateReporter(
        string team = "Smoke",
        string system = "QaaS",
        string? project = null,
        string endpoint = "http://localhost:8080")
    {
        var context = new InternalContext
        {
            Logger = Globals.Logger,
            RootConfiguration = new ConfigurationBuilder().Build()
        };
        context.InsertValueIntoGlobalDictionary(context.GetMetaDataPath(), new MetaDataConfig
        {
            Team = team,
            System = system
        });

        return new ReportPortalReporter
        {
            Config = new ReportPortalConfig
            {
                Enabled = true,
                Endpoint = endpoint,
                ApiKey = "api-key",
                Project = project
            },
            Context = context,
            ExecutionMode = "run"
        };
    }

    private static AssertionResult CreateResult(string assertionName, string sessionName)
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
            TestDurationMs = 1,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] }
        };
    }

    private sealed class RecordingClientFactory : IReportPortalClientFactory
    {
        public bool ThrowOnLaunchStart { get; init; }
        public bool ThrowOnDispose { get; init; }
        public List<RecordingClientService> Services { get; } = [];

        public IClientService Create(Uri endpointUri, string project, string apiKey)
        {
            var service = new RecordingClientService(ThrowOnLaunchStart, ThrowOnDispose);
            Services.Add(service);
            return service.Object;
        }
    }

    private sealed class RecordingClientService
    {
        private readonly Mock<IClientService> _service = new();
        private readonly Mock<ILaunchResource> _launchResource = new();
        private readonly Mock<ITestItemResource> _testItemResource = new();
        private readonly Mock<ILogItemResource> _logItemResource = new();

        public RecordingClientService(bool throwOnLaunchStart, bool throwOnDispose)
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
                .ReturnsAsync(new LaunchFinishedResponse { Uuid = "launch" });

            _testItemResource
                .Setup(resource => resource.StartAsync(It.IsAny<StartTestItemRequest>(),
                    It.IsAny<CancellationToken>()))
                .Callback<StartTestItemRequest, CancellationToken>((request, _) => TestItemStartRequests.Add(request))
                .ReturnsAsync(() => new TestItemCreatedResponse
                {
                    Uuid = $"item-{TestItemStartRequests.Count}"
                });
            _testItemResource
                .Setup(resource => resource.FinishAsync(It.IsAny<string>(), It.IsAny<FinishTestItemRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MessageResponse());

            _logItemResource
                .Setup(resource => resource.CreateAsync(It.IsAny<CreateLogItemRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LogItemCreatedResponse { Uuid = "log" });
        }

        public IClientService Object => _service.Object;
        public int DisposeCount { get; private set; }
        public List<StartLaunchRequest> LaunchStartRequests { get; } = [];
        public List<StartTestItemRequest> TestItemStartRequests { get; } = [];
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responseFactory(request));
        }
    }

    private static void VerifyWarningLogged(Mock<ILogger> logger, string messageFragment)
    {
        logger.Verify(
            item => item.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) =>
                    value.ToString()!.Contains(messageFragment, StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
