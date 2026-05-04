using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.LinkBuilders;
using QaaS.Runner.Assertions.Reporters;
using QaaS.Runner.Assertions.Tests.Mocks;
using QaaS.Runner.Infrastructure;
using ReportPortal.Client.Abstractions.Models;
using ReportPortal.Client.Abstractions.Requests;
using ReportPortal.Client.Abstractions.Responses;
using RpStatus = ReportPortal.Client.Abstractions.Models.Status;

namespace QaaS.Runner.Assertions.Tests.AssertionResultsWritersTests;

[TestFixture]
public class ReportPortalReporterTests
{
    private string _configPath = null!;

    [SetUp]
    public void SetUp()
    {
        _configPath = Path.Combine(TestContext.CurrentContext.WorkDirectory,
            $"{Guid.NewGuid():N}.reportportal.config.json");
        File.WriteAllText(_configPath, """
                                       {
                                         "enabled": true,
                                         "server": {
                                           "url": "http://reportportal.local",
                                           "apiKey": "rp-token"
                                         },
                                         "launch": {
                                           "debugMode": true
                                         }
                                       }
                                       """);
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_configPath))
            File.Delete(_configPath);
    }

    [Test]
    public void WriteTestResults_StartsLaunchFromConfigAndMetadata()
    {
        var client = new RecordingReportPortalClient();
        var factory = new RecordingReportPortalClientFactory(client);
        var reporter = CreateReporter(factory);

        reporter.WriteTestResults(CreateAssertionResult());
        reporter.FinishReport();

        Assert.Multiple(() =>
        {
            Assert.That(factory.ServerUrl, Is.EqualTo("http://reportportal.local"));
            Assert.That(factory.ProjectName, Is.EqualTo("Smoke"));
            Assert.That(factory.ApiKey, Is.EqualTo("rp-token"));
            Assert.That(client.StartLaunchRequests.Single().Mode, Is.EqualTo(LaunchMode.Debug));
            Assert.That(client.StartLaunchRequests.Single().Name, Is.EqualTo("Smoke QaaS"));
            Assert.That(client.StartLaunchRequests.Single().Attributes.Select(attribute => attribute.Key),
                Does.Contain("system"));
            Assert.That(client.FinishLaunchRequests, Has.Count.EqualTo(1));
            Assert.That(client.Disposed, Is.True);
        });
    }

    [Test]
    public void WriteTestResults_WithMissingMetadataTeam_ThrowsInvalidOperationException()
    {
        var context = CreateContext(metadataTeam: string.Empty);
        var reporter = CreateReporter(new RecordingReportPortalClientFactory(new RecordingReportPortalClient()),
            context: context,
            startReport: false);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            reporter.StartReport(context, TestSuiteStartTime));

        Assert.That(exception!.Message, Does.Contain("metadata Team"));
    }

    [Test]
    public void WriteTestResults_WithMissingServerUrl_ThrowsInvalidOperationException()
    {
        File.WriteAllText(_configPath, """
                                       {
                                         "server": {
                                           "apiKey": "rp-token"
                                         }
                                       }
                                       """);
        var context = CreateContext();
        var reporter = CreateReporter(new RecordingReportPortalClientFactory(new RecordingReportPortalClient()),
            context: context,
            startReport: false);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            reporter.StartReport(context, TestSuiteStartTime));

        Assert.That(exception!.Message, Does.Contain("server URL"));
    }

    [Test]
    public void WriteTestResults_WhenReportPortalIsDisabled_DoesNotStartLaunchOrWriteItems()
    {
        File.WriteAllText(_configPath, """
                                       {
                                         "enabled": false,
                                         "server": {
                                           "url": "http://reportportal.local",
                                           "apiKey": "rp-token"
                                         }
                                       }
                                       """);
        var client = new RecordingReportPortalClient();
        var reporter = CreateReporter(new RecordingReportPortalClientFactory(client), startReport: false);

        reporter.StartReport(reporter.Context, TestSuiteStartTime);
        reporter.WriteTestResults(CreateAssertionResult());
        reporter.FinishReport();

        Assert.Multiple(() =>
        {
            Assert.That(client.StartLaunchRequests, Is.Empty);
            Assert.That(client.StartTestItemRequests, Is.Empty);
            Assert.That(client.FinishLaunchRequests, Is.Empty);
            Assert.That(client.Disposed, Is.False);
        });
    }

    [Test]
    public void WriteTestResults_MapsGenericReportCaseToReportPortalRequests()
    {
        var client = new RecordingReportPortalClient();
        var reporter = CreateReporter(new RecordingReportPortalClientFactory(client));

        reporter.WriteTestResults(CreateAssertionResult());
        reporter.FinishReport();

        var testRequest = client.StartTestItemRequests.Single();
        var testFinish = client.FinishTestItemRequests.Last();
        var childRequests = client.StartChildTestItemRequests.Select(request => request.Request).ToList();
        var logRequests = client.CreateLogItemRequests;

        Assert.Multiple(() =>
        {
            Assert.That(testRequest.Name, Is.EqualTo("report-portal-assertion"));
            Assert.That(testRequest.UniqueId, Is.EqualTo("report-portal-assertionexecution-1case-1"));
            Assert.That(testRequest.TestCaseId, Is.EqualTo(testRequest.UniqueId));
            Assert.That(testRequest.Parameters.Select(parameter => parameter.Key),
                Is.EquivalentTo(new[] { "Session Names", "Data Sources" }));
            Assert.That(testRequest.Attributes.Select(attribute => attribute.Key),
                Does.Contain("severity").And.Contain("assertionType").And.Contain("flaky")
                    .And.Contain("system").And.Contain("executionId").And.Contain("caseName"));
            Assert.That(testFinish.Request.Status, Is.EqualTo(RpStatus.Failed));
            Assert.That(childRequests.Select(request => request.Name),
                Does.Contain("session-1").And.Contain(nameof(SessionData.SessionFailures)));
            Assert.That(logRequests.Select(request => request.Text), Has.Some.Contains("Description:"));
            Assert.That(logRequests.Select(request => request.Text), Has.Some.Contains("Status Message:"));
            Assert.That(logRequests.Select(request => request.Text), Has.Some.Contains("Trace:"));
            Assert.That(logRequests.Select(request => request.Text), Has.Some.Contains("Link: docs"));
            Assert.That(logRequests.Where(request => request.Attach != null).Select(request => request.Attach!.Name),
                Does.Contain("payload.json").And.Contain("session-1.json").And.Contain("session-1.log"));
        });
    }

    private static readonly DateTime TestSuiteStartTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private ReportPortalReporter CreateReporter(
        RecordingReportPortalClientFactory factory,
        string metadataTeam = "Smoke",
        InternalContext? context = null,
        bool startReport = true)
    {
        context ??= CreateContext(metadataTeam);
        var reporter = new ReportPortalReporter(factory, new System.IO.Abstractions.FileSystem(), _configPath)
        {
            Context = context,
            FileSystem = new System.IO.Abstractions.FileSystem(),
            SaveAttachments = true,
            SaveLogs = true,
            SaveSessionData = true,
            SaveTemplate = false,
            DisplayTrace = true,
            Severity = AssertionSeverity.Critical,
            TestSuiteStartTimeUtc = TestSuiteStartTime
        };

        if (startReport)
            reporter.StartReport(context, TestSuiteStartTime);

        return reporter;
    }

    private static InternalContext CreateContext(string metadataTeam = "Smoke")
    {
        var context = new InternalContext
        {
            Logger = Globals.Logger,
            RootConfiguration = new ConfigurationBuilder().Build(),
            ExecutionId = "execution-1",
            CaseName = "case-1"
        };
        context.InsertValueIntoGlobalDictionary(context.GetMetaDataPath(), new MetaDataConfig
        {
            Team = metadataTeam,
            System = "QaaS"
        });
        context.AppendSessionLog("session-1", "session log line");

        return context;
    }

    private static AssertionResult CreateAssertionResult()
    {
        var sessionData = new SessionData
        {
            Name = "session-1",
            UtcStartTime = new DateTime(2026, 1, 1, 12, 0, 1, DateTimeKind.Utc),
            UtcEndTime = new DateTime(2026, 1, 1, 12, 0, 3, DateTimeKind.Utc),
            SessionFailures =
            [
                new ActionFailure
                {
                    Name = "publish",
                    Action = "publish",
                    ActionType = "Publisher",
                    Reason = new Reason
                    {
                        Message = "message",
                        Description = "description"
                    }
                }
            ]
        };

        return new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "report-portal-assertion",
                AssertionName = "AssertionHookMock",
                AssertionHook = new AssertionHookMock
                {
                    AssertionMessage = "assertion message",
                    AssertionTrace = "assertion trace",
                    AssertionAttachments =
                    [
                        new AssertionAttachment
                        {
                            Path = "payloads/payload.json",
                            SerializationType = SerializationType.Json,
                            Data = new { Value = 5 }
                        }
                    ]
                },
                SessionDataList = ImmutableList.Create(sessionData),
                DataSourceList = ImmutableList<QaaS.Framework.SDK.DataSourceObjects.DataSource>.Empty,
                StatusesToReport =
                [
                    AssertionStatus.Broken,
                    AssertionStatus.Failed,
                    AssertionStatus.Passed,
                    AssertionStatus.Skipped,
                    AssertionStatus.Unknown
                ],
                Links =
                [
                    new TestLink("docs", "https://example.test/docs")
                ]
            },
            AssertionStatus = AssertionStatus.Failed,
            TestDurationMs = 3000,
            Flaky = new Flaky
            {
                IsFlaky = true,
                FlakinessReasons =
                [
                    new KeyValuePair<string, List<ActionFailure>>("session-1", sessionData.SessionFailures)
                ]
            },
            Links =
            [
                new KeyValuePair<string, string>("docs", "https://example.test/docs")
            ]
        };
    }

    private sealed class TestLink(string name, string url) : BaseLink(name)
    {
        protected override string BuildLink(IList<KeyValuePair<DateTime, DateTime>> startEndTimesKeyValuePairs)
        {
            return url;
        }
    }

    internal sealed class RecordingReportPortalClientFactory(RecordingReportPortalClient client) : IReportPortalClientFactory
    {
        public string? ServerUrl { get; private set; }
        public string? ProjectName { get; private set; }
        public string? ApiKey { get; private set; }

        public IReportPortalClient Create(string serverUrl, string projectName, string apiKey)
        {
            ServerUrl = serverUrl;
            ProjectName = projectName;
            ApiKey = apiKey;
            return client;
        }
    }

    internal sealed class RecordingReportPortalClient : IReportPortalClient
    {
        private int _nextId;
        public List<StartLaunchRequest> StartLaunchRequests { get; } = [];
        public List<FinishLaunchRequest> FinishLaunchRequests { get; } = [];
        public List<StartTestItemRequest> StartTestItemRequests { get; } = [];
        public List<(string ParentUuid, StartTestItemRequest Request)> StartChildTestItemRequests { get; } = [];
        public List<(string Uuid, FinishTestItemRequest Request)> FinishTestItemRequests { get; } = [];
        public List<CreateLogItemRequest> CreateLogItemRequests { get; } = [];
        public bool Disposed { get; private set; }

        public Task<LaunchCreatedResponse> StartLaunchAsync(StartLaunchRequest request)
        {
            StartLaunchRequests.Add(request);
            return Task.FromResult(new LaunchCreatedResponse { Uuid = "launch-uuid" });
        }

        public Task<LaunchFinishedResponse> FinishLaunchAsync(string launchUuid, FinishLaunchRequest request)
        {
            FinishLaunchRequests.Add(request);
            return Task.FromResult(new LaunchFinishedResponse { Uuid = launchUuid });
        }

        public Task<TestItemCreatedResponse> StartTestItemAsync(StartTestItemRequest request)
        {
            StartTestItemRequests.Add(request);
            return Task.FromResult(new TestItemCreatedResponse { Uuid = $"item-{++_nextId}" });
        }

        public Task<TestItemCreatedResponse> StartChildTestItemAsync(string parentItemUuid, StartTestItemRequest request)
        {
            StartChildTestItemRequests.Add((parentItemUuid, request));
            return Task.FromResult(new TestItemCreatedResponse { Uuid = $"item-{++_nextId}" });
        }

        public Task<MessageResponse> FinishTestItemAsync(string itemUuid, FinishTestItemRequest request)
        {
            FinishTestItemRequests.Add((itemUuid, request));
            return Task.FromResult(new MessageResponse { Info = "finished" });
        }

        public Task<LogItemCreatedResponse> CreateLogItemAsync(CreateLogItemRequest request)
        {
            CreateLogItemRequests.Add(request);
            return Task.FromResult(new LogItemCreatedResponse { Uuid = $"log-{CreateLogItemRequests.Count}" });
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
