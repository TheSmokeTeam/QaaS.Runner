using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.Commons;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.Reporters;
using QaaS.Runner.Assertions.Reporters.Allure;
using QaaS.Runner.Assertions.Tests.Mocks;
using QaaS.Runner.Infrastructure;

namespace QaaS.Runner.Assertions.Tests.AssertionResultsWritersTests;

[TestFixture]
public class AllureReporterTests
{
    [SetUp]
    public void SetUp()
    {
        if (FileSystem.Directory.Exists(AllureResultsFolder))
            FileSystem.Directory.Delete(AllureResultsFolder, true);
        // Setup reporter with mocked dependencies
        Reporter = new AllureReporter
        {
            Context = new Context { Logger = Globals.Logger },
            SaveLogs = true,
            SaveAttachments = true,
            FileSystem = new FileSystem(),
        };
    }

    [TearDown]
    public void DeleteAllureDirectoryIfExists()
    {
        if (FileSystem.Directory.Exists(AllureResultsFolder))
            FileSystem.Directory.Delete(AllureResultsFolder, true);
    }

    public AllureReporter? Reporter;

    private static readonly IFileSystem FileSystem = new FileSystem();
    private const string AllureResultsFolder = AllureConstants.DEFAULT_RESULTS_FOLDER;

    private static string NormalizePathSeparators(string value)
    {
        return value.Replace('\\', '/');
    }

    private static void AssertAllureAttachmentSourceContract(IEnumerable<string> sources)
    {
        var resultsDirectory = Path.GetFullPath(AllureResultsFolder);
        foreach (var source in sources)
        {
            Assert.That(source, Does.Match("^[a-zA-Z0-9._-]{1,100}$"));
            Assert.That(Path.GetFileName(source), Is.EqualTo(source));
            var attachmentPath = Path.GetFullPath(Path.Combine(resultsDirectory, source));
            Assert.That(Path.GetDirectoryName(attachmentPath), Is.EqualTo(resultsDirectory));
            Assert.That(File.Exists(attachmentPath), Is.True);
        }
    }

    private static string GetAllureParameter(JsonElement step, string parameterName)
    {
        return step.GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == parameterName)
            .GetProperty("value")
            .GetString()!;
    }

    private static IEnumerable<JsonElement> GetAttachments(JsonElement executableItem)
    {
        if (
            executableItem.TryGetProperty("attachments", out var attachments)
            && attachments.ValueKind == JsonValueKind.Array
        )
            foreach (var attachment in attachments.EnumerateArray())
                yield return attachment;

        if (
            executableItem.TryGetProperty("steps", out var steps)
            && steps.ValueKind == JsonValueKind.Array
        )
            foreach (var step in steps.EnumerateArray())
            foreach (var attachment in GetAttachments(step))
                yield return attachment;
    }

    private static IEnumerable<string> GetAttachmentSources(JsonElement executableItem)
    {
        return GetAttachments(executableItem)
            .Select(attachment => attachment.GetProperty("source").GetString()!);
    }

    private static IEnumerable<TestCaseData> TestWriteTestResultsEnumerableCaseSource()
    {
        yield return new TestCaseData(
            new Context[] { new() { Logger = Globals.Logger } },
            new AssertionResult
            {
                Assertion = new Assertion
                {
                    Name = "Test",
                    AssertionName = "AssertionOne",
                    SessionDataList = [],
                    AssertionHook = null,
                    StatusesToReport = null,
                },
                AssertionStatus = AssertionStatus.Passed,
                TestDurationMs = 10,
                Flaky = new Flaky
                {
                    IsFlaky = false,
                    FlakinessReasons = new List<KeyValuePair<string, List<ActionFailure>>>(),
                },
            },
            false,
            true,
            2
        ).SetName("NoSessionData");

        yield return new TestCaseData(
            new Context[] { new() { Logger = Globals.Logger } },
            new AssertionResult
            {
                Assertion = new Assertion
                {
                    Name = "Test",
                    AssertionName = "AssertionOne",
                    SessionDataList = new List<SessionData>
                    {
                        new() { Name = "test", SessionFailures = new List<ActionFailure>() },
                    }.ToImmutableList(),
                    AssertionHook = null,
                    StatusesToReport = null,
                },

                AssertionStatus = AssertionStatus.Passed,
                TestDurationMs = 10,
                Flaky = new Flaky
                {
                    IsFlaky = false,
                    FlakinessReasons = new List<KeyValuePair<string, List<ActionFailure>>>(),
                },
            },
            true,
            false,
            2
        ).SetName("WithOneSessionData");

        yield return new TestCaseData(
            new Context[]
            {
                new() { Logger = Globals.Logger, CaseName = "test" },
                new() { Logger = Globals.Logger, CaseName = "test2" },
            },
            new AssertionResult
            {
                Assertion = new Assertion
                {
                    Name = "Test",
                    AssertionName = "AssertionOne",
                    SessionDataList = new List<SessionData>
                    {
                        new() { Name = "test", SessionFailures = new List<ActionFailure>() },
                    }.ToImmutableList(),
                    AssertionHook = null,
                    StatusesToReport = null,
                },
                AssertionStatus = AssertionStatus.Passed,
                TestDurationMs = 10,
                Flaky = new Flaky
                {
                    IsFlaky = false,
                    FlakinessReasons = new List<KeyValuePair<string, List<ActionFailure>>>(),
                },
            },
            true,
            true,
            6
        ).SetName("MultipleCasesWithOneSameSessionData");

        yield return new TestCaseData(
            new Context[]
            {
                new()
                {
                    Logger = Globals.Logger,
                    ExecutionId = "test",
                    CaseName = "a",
                },
                new()
                {
                    Logger = Globals.Logger,
                    ExecutionId = "test2",
                    CaseName = "a",
                },
            },
            new AssertionResult
            {
                Assertion = new Assertion
                {
                    Name = "Test",
                    AssertionName = "AssertionOne",
                    SessionDataList = new List<SessionData>
                    {
                        new() { Name = "test", SessionFailures = new List<ActionFailure>() },
                    }.ToImmutableList(),
                    AssertionHook = null,
                    StatusesToReport = null,
                },
                AssertionStatus = AssertionStatus.Passed,
                TestDurationMs = 10,
                Flaky = new Flaky
                {
                    IsFlaky = false,
                    FlakinessReasons = new List<KeyValuePair<string, List<ActionFailure>>>(),
                },
            },
            true,
            false,
            4
        ).SetName("MultipleExecutionsSameCasesWithOneSameSessionData");

        yield return new TestCaseData(
            new Context[] { new() { Logger = Globals.Logger } },
            new AssertionResult
            {
                Assertion = new Assertion
                {
                    Name = "Test",
                    AssertionName = "AssertionOne",
                    SessionDataList = new List<SessionData>
                    {
                        new() { Name = "test", SessionFailures = new List<ActionFailure>() },
                        new() { Name = "test2", SessionFailures = new List<ActionFailure>() },
                        new() { Name = "test3", SessionFailures = new List<ActionFailure>() },
                    }.ToImmutableList(),
                    AssertionHook = null,
                    StatusesToReport = null,
                },
                AssertionStatus = AssertionStatus.Passed,
                TestDurationMs = 10,
                Flaky = new Flaky
                {
                    IsFlaky = false,
                    FlakinessReasons = new List<KeyValuePair<string, List<ActionFailure>>>(),
                },
            },
            true,
            false,
            4
        ).SetName("WithMultipleSessionData");

        yield return new TestCaseData(
            new Context[] { new() { Logger = Globals.Logger } },
            new AssertionResult
            {
                Assertion = new Assertion
                {
                    Name = "Test",
                    AssertionName = "AssertionOne",
                    SessionDataList = new List<SessionData>
                    {
                        new() { Name = "test", SessionFailures = new List<ActionFailure>() },
                        new() { Name = "test2", SessionFailures = new List<ActionFailure>() },
                        new() { Name = "test3", SessionFailures = new List<ActionFailure>() },
                        new() { Name = "test", SessionFailures = new List<ActionFailure>() },
                        new() { Name = "test2", SessionFailures = new List<ActionFailure>() },
                        new() { Name = "test3", SessionFailures = new List<ActionFailure>() },
                    }.ToImmutableList(),
                    AssertionHook = null,
                    StatusesToReport = null,
                },
                AssertionStatus = AssertionStatus.Passed,
                TestDurationMs = 10,
                Flaky = new Flaky
                {
                    IsFlaky = false,
                    FlakinessReasons = new List<KeyValuePair<string, List<ActionFailure>>>(),
                },
            },
            true,
            true,
            5
        ).SetName("WithMultipleSessionDataDuplicated");
    }

    [Test]
    [TestCaseSource(nameof(TestWriteTestResultsEnumerableCaseSource))]
    [Order(1)]
    public void TestWriteTestResults_ForEachContextCallFunctionWithAssertionResult_ShouldWriteExpectedNumberOfAttachmentsForIt(
        IEnumerable<Context> contexts,
        AssertionResult assertionResult,
        bool saveSessionData,
        bool saveTemplate,
        int expectedItemCount
    )
    {
        // Arrange
        List<AllureReporter> allureResultsHandlers = [];
        foreach (var context in contexts)
            allureResultsHandlers.Add(
                new AllureReporter
                {
                    Context = context,
                    SaveAttachments = false,
                    SaveTemplate = saveTemplate,
                    SaveSessionData = saveSessionData,
                    FileSystem = new FileSystem(),
                }
            );

        // Act
        foreach (var allureResultsHandler in allureResultsHandlers)
            allureResultsHandler.WriteTestResults(assertionResult);

        // Assert
        Assert.AreEqual(
            expectedItemCount,
            FileSystem
                .Directory.GetFiles(AllureResultsFolder, "", SearchOption.TopDirectoryOnly)
                .Length
        );
    }

    [Test]
    public void WriteTestResults_RepeatedLogicalTest_UsesDistinctUuidsAndStableHistoryId()
    {
        Reporter!.SaveSessionData = false;
        Reporter.SaveLogs = false;
        Reporter.SaveTemplate = false;
        Reporter.SaveAttachments = false;
        Reporter.Context = new Context
        {
            Logger = Globals.Logger,
            ExecutionId = "exec-1",
            CaseName = "case-a",
        };
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "stable-assertion",
                AssertionName = "StableAssertion",
                SessionDataList = [],
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 10,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        Reporter.WriteTestResults(assertionResult);
        Reporter.WriteTestResults(assertionResult);

        var uuids = new List<string>();
        var historyIds = new List<string>();
        var hasNonNullRerunOf = false;
        foreach (
            var resultFile in Directory.GetFiles(
                AllureResultsFolder,
                "*-result.json",
                SearchOption.TopDirectoryOnly
            )
        )
        {
            using var resultDocument = JsonDocument.Parse(File.ReadAllText(resultFile));
            uuids.Add(resultDocument.RootElement.GetProperty("uuid").GetString()!);
            historyIds.Add(resultDocument.RootElement.GetProperty("historyId").GetString()!);
            hasNonNullRerunOf |=
                resultDocument.RootElement.TryGetProperty("rerunOf", out var rerunOf)
                && rerunOf.ValueKind != JsonValueKind.Null;
        }

        Assert.Multiple(() =>
        {
            Assert.That(uuids, Has.Count.EqualTo(2));
            Assert.That(
                uuids,
                Has.All.Matches<string>(uuid => Guid.TryParseExact(uuid, "D", out _))
            );
            Assert.That(uuids, Is.Unique);
            Assert.That(
                historyIds,
                Is.EqualTo(new[] { "stable-assertionexec-1case-a", "stable-assertionexec-1case-a" })
            );
            Assert.That(hasNonNullRerunOf, Is.False);
        });
    }

    [Test]
    [TestCase("BaseDirectory", "SpecificAssertion", "Run", "Case1")]
    [TestCase("BaseDirectory", "SpecificAssertion", "Run", null)]
    [TestCase("AssertionDirectory", null, null, "Case3")]
    [TestCase("AssertionDirectory")]
    public void TestGetAttachmentDirectory_CallFunctionWithBaseAndExtraDirectoriesWithDifferentContexts_ShouldReturnCorrectAttachmentDirectory(
        string baseAttachmentDirectoryInsideAllureDirectory,
        string? extraSubDirectoryName = null,
        string? executionId = null,
        string? caseName = null
    )
    {
        var context = new Context
        {
            Logger = Globals.Logger,
            ExecutionId = executionId,
            CaseName = caseName,
        };
        Reporter?.Context = context;
        // Arrange
        var getAttachmentDirectoryMethod = Reporter
            ?.GetType()
            .GetMethod("GetAttachmentDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var epochTestSuiteStartTimeField = typeof(AllureReporter).GetProperty(
            "EpochTestSuiteStartTime",
            BindingFlags.Public | BindingFlags.Instance
        )!;

        // Act
        var attachmentDirectory = (string)
            getAttachmentDirectoryMethod.Invoke(
                Reporter,
                [baseAttachmentDirectoryInsideAllureDirectory, extraSubDirectoryName]
            )!;
        var expectedAttachmentDirectory = Path.Join(
            baseAttachmentDirectoryInsideAllureDirectory,
            epochTestSuiteStartTimeField.GetValue(Reporter)!.ToString(),
            FileSystemExtensions.MakeValidDirectoryName(executionId),
            FileSystemExtensions.MakeValidDirectoryName(caseName),
            FileSystemExtensions.MakeValidDirectoryName(extraSubDirectoryName)
        );
        // Assert
        Assert.AreEqual(expectedAttachmentDirectory, attachmentDirectory);
    }

    [Test]
    public void GetAttachmentDirectory_WithNullBaseDirectory_ThrowsInvalidOperationException()
    {
        var method = Reporter!
            .GetType()
            .GetMethod("GetAttachmentDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(Reporter, new object?[] { null, null })
        );

        Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void WriteTestResults_WithTraversalInCustomAttachmentPath_ThrowsInvalidOperationException()
    {
        Reporter!.SaveSessionData = false;
        Reporter.SaveTemplate = false;
        Reporter.SaveAttachments = true;
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                AssertionHook = new AssertionHookMock
                {
                    AssertionAttachments =
                    [
                        new AssertionAttachment
                        {
                            Path = "../outside.txt",
                            SerializationType = SerializationType.Json,
                            Data = new byte[] { 0x01 },
                        },
                    ],
                },
                Name = "unsafe-assertion",
                AssertionName = "AssertionOne",
                SessionDataList = [],
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        Assert.Throws<InvalidOperationException>(() => Reporter.WriteTestResults(assertionResult));
    }

    [Test]
    public void WriteTestResults_WithRenderedTemplate_UsesStoredTemplateContent()
    {
        const string renderedTemplate = "Sessions:\n  - Name: RabbitRoundTrip\n";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Sessions:0:Name"] = "incomplete" }
            )
            .Build();
        Reporter!.SaveSessionData = false;
        Reporter.SaveTemplate = true;
        Reporter.SaveAttachments = false;
        Reporter.Context = new Context
        {
            Logger = Globals.Logger,
            RootConfiguration = configuration,
        };
        Reporter.Context.InsertValueIntoGlobalDictionary(
            ["__RunnerArtifacts", "RenderedTemplate"],
            renderedTemplate
        );
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "template-assertion",
                AssertionName = "TemplateAssertion",
                SessionDataList = [],
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        Reporter.WriteTestResults(assertionResult);
        var resultFile = Directory
            .GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly)
            .Single();
        using var resultDocument = JsonDocument.Parse(File.ReadAllText(resultFile));
        var attachment = resultDocument
            .RootElement.GetProperty("attachments")
            .EnumerateArray()
            .Single();
        var attachmentSource = attachment.GetProperty("source").GetString()!;
        var attachmentPath = Path.Combine(AllureResultsFolder, attachmentSource);

        Assert.Multiple(() =>
        {
            AssertAllureAttachmentSourceContract([attachmentSource]);
            Assert.That(attachment.GetProperty("type").GetString(), Is.EqualTo("application/yaml"));
            Assert.That(File.Exists(attachmentPath), Is.True);
            Assert.That(File.ReadAllText(attachmentPath), Is.EqualTo(renderedTemplate));
        });
    }

    [Test]
    public void WriteTestResults_WithStoredSessionLogs_PersistsGeneratedSessionLogAttachment()
    {
        Reporter!.SaveSessionData = false;
        Reporter.SaveLogs = true;
        Reporter.SaveTemplate = false;
        Reporter.SaveAttachments = false;
        Reporter.Context.AppendSessionLog("test-session", "Starting session test-session");
        Reporter.Context.AppendSessionLog("test-session", "Session test-session completed.");
        var sessionData = new SessionData
        {
            Name = "test-session",
            UtcStartTime = DateTime.UtcNow.AddSeconds(-1),
            UtcEndTime = DateTime.UtcNow,
            SessionFailures = [],
        };
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "log-assertion",
                AssertionName = "LogAssertion",
                SessionDataList = new List<SessionData> { sessionData }.ToImmutableList(),
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 10,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        Reporter.WriteTestResults(assertionResult);
        var resultFile = Directory
            .GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly)
            .Single();
        var logAttachmentPath = Directory
            .GetFiles(AllureResultsFolder, "*-attachment.log", SearchOption.TopDirectoryOnly)
            .Single();

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(resultFile), Does.Contain("SessionLog"));
            Assert.That(
                File.ReadAllText(resultFile),
                Does.Contain(Path.GetFileName(logAttachmentPath))
            );
            Assert.That(
                Directory.Exists(Path.Combine(AllureResultsFolder, "SessionLogs")),
                Is.True
            );
            Assert.That(File.Exists(logAttachmentPath), Is.True);
            Assert.That(
                File.ReadAllText(logAttachmentPath),
                Does.Contain("Starting session test-session")
            );
            Assert.That(
                File.ReadAllText(logAttachmentPath),
                Does.Contain("Session test-session completed.")
            );
        });
    }

    [Test]
    public void WriteTestResults_WithStoredSessionLogsAndSaveLogsDisabled_DoesNotPersistSessionLogAttachment()
    {
        Reporter!.SaveSessionData = false;
        Reporter.SaveLogs = false;
        Reporter.SaveTemplate = false;
        Reporter.SaveAttachments = false;
        Reporter.Context.AppendSessionLog("test-session", "Starting session test-session");
        Reporter.Context.AppendSessionLog("test-session", "Session test-session completed.");
        var sessionData = new SessionData
        {
            Name = "test-session",
            UtcStartTime = DateTime.UtcNow.AddSeconds(-1),
            UtcEndTime = DateTime.UtcNow,
            SessionFailures = [],
        };
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "log-assertion",
                AssertionName = "LogAssertion",
                SessionDataList = new List<SessionData> { sessionData }.ToImmutableList(),
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 10,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        Reporter.WriteTestResults(assertionResult);
        var resultFile = Directory
            .GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly)
            .Single();

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(resultFile), Does.Not.Contain("SessionLog"));
            Assert.That(
                Directory.Exists(Path.Combine(AllureResultsFolder, "SessionLogs")),
                Is.False
            );
            Assert.That(
                Directory.GetFiles(
                    AllureResultsFolder,
                    "*-attachment*",
                    SearchOption.TopDirectoryOnly
                ),
                Is.Empty
            );
        });
    }

    [Test]
    public void WriteTestResults_WithSessionArtifacts_PreservesLegacyCopiesAndUsesAllureCompatibleSources()
    {
        Reporter!.SaveSessionData = true;
        Reporter.SaveTemplate = true;
        Reporter.SaveAttachments = false;
        Reporter.Context = new Context
        {
            Logger = Globals.Logger,
            RootConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Sessions:0:Name"] = "RabbitRoundTrip" }
                )
                .Build(),
        };
        Reporter.Context.AppendSessionLog("test-session", "session-log-entry");
        var sessionData = new SessionData
        {
            Name = "test-session",
            UtcStartTime = DateTime.UtcNow.AddSeconds(-1),
            UtcEndTime = DateTime.UtcNow,
            SessionFailures = [],
        };
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "artifact-assertion",
                AssertionName = "ArtifactAssertion",
                SessionDataList = new List<SessionData> { sessionData }.ToImmutableList(),
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 10,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        Reporter.WriteTestResults(assertionResult);
        var resultFile = Directory
            .GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly)
            .Single();
        var contents = File.ReadAllText(resultFile);
        using var resultDocument = JsonDocument.Parse(contents);
        var attachments = GetAttachments(resultDocument.RootElement).ToList();
        var attachmentSources = attachments
            .Select(attachment => attachment.GetProperty("source").GetString()!)
            .ToList();
        var sessionsDataAttachment = Directory
            .GetFiles(AllureResultsFolder, "*-attachment.json", SearchOption.TopDirectoryOnly)
            .Single();
        var sessionLogAttachment = Directory
            .GetFiles(AllureResultsFolder, "*-attachment.log", SearchOption.TopDirectoryOnly)
            .Single();
        var templateAttachment = Directory
            .GetFiles(AllureResultsFolder, "*-attachment.yaml", SearchOption.TopDirectoryOnly)
            .Single();

        Assert.Multiple(() =>
        {
            AssertAllureAttachmentSourceContract(attachmentSources);
            Assert.That(attachmentSources, Has.Count.EqualTo(3));
            Assert.That(
                attachments.Select(attachment => attachment.GetProperty("type").GetString()),
                Is.EquivalentTo(new[] { "application/json", "text/plain", "application/yaml" })
            );
            Assert.That(
                attachmentSources,
                Is.EquivalentTo(
                    new[]
                    {
                        Path.GetFileName(sessionsDataAttachment),
                        Path.GetFileName(sessionLogAttachment),
                        Path.GetFileName(templateAttachment),
                    }
                )
            );
            Assert.That(
                File.ReadAllText(sessionsDataAttachment),
                Does.Contain("\"Name\": \"test-session\"")
            );
            Assert.That(File.ReadAllText(sessionLogAttachment), Does.Contain("session-log-entry"));
            Assert.That(File.ReadAllText(templateAttachment), Does.Contain("RabbitRoundTrip"));
            Assert.That(
                Directory.Exists(Path.Combine(AllureResultsFolder, "SessionsData")),
                Is.True
            );
            Assert.That(
                Directory.Exists(Path.Combine(AllureResultsFolder, "SessionLogs")),
                Is.True
            );
            Assert.That(Directory.Exists(Path.Combine(AllureResultsFolder, "Templates")), Is.True);
        });
    }

    [Test]
    public void WriteTestResults_WithCustomAttachments_PreservesLegacyCopyAndUsesAllureCompatibleSource()
    {
        Reporter!.SaveSessionData = false;
        Reporter.SaveTemplate = false;
        Reporter.SaveAttachments = true;
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "custom-attachments",
                AssertionName = "CustomAttachmentAssertion",
                AssertionHook = new AssertionHookMock
                {
                    AssertionAttachments =
                    [
                        new AssertionAttachment
                        {
                            Path = "payloads/payload.json",
                            SerializationType = SerializationType.Json,
                            Data = new { Value = 5 },
                        },
                    ],
                },
                SessionDataList = [],
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 10,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        Reporter.WriteTestResults(assertionResult);
        var resultFile = Directory
            .GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly)
            .Single();
        var attachmentFile = Directory
            .GetFiles(AllureResultsFolder, "*-attachment.json", SearchOption.TopDirectoryOnly)
            .Single();
        var contents = File.ReadAllText(resultFile);
        using var resultDocument = JsonDocument.Parse(contents);
        var attachment = GetAttachments(resultDocument.RootElement).Single();
        var attachmentSource = attachment.GetProperty("source").GetString()!;

        Assert.Multiple(() =>
        {
            Assert.That(attachmentSource, Is.EqualTo(Path.GetFileName(attachmentFile)));
            AssertAllureAttachmentSourceContract([attachmentSource]);
            Assert.That(
                NormalizePathSeparators(attachment.GetProperty("name").GetString()!),
                Is.EqualTo("payloads/payload.json")
            );
            Assert.That(attachment.GetProperty("type").GetString(), Is.EqualTo("application/json"));
            Assert.That(
                Directory.Exists(Path.Combine(AllureResultsFolder, "AssertionsAttachments")),
                Is.True
            );
            Assert.That(File.ReadAllText(attachmentFile), Does.Contain("\"Value\":5"));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(5)]
    [TestCase(10)]
    public void WriteTestResults_WithMultipleCustomAttachments_WritesEveryAttachment(
        int attachmentCount
    )
    {
        Reporter!.SaveSessionData = false;
        Reporter.SaveTemplate = false;
        Reporter.SaveAttachments = true;
        var assertionAttachments = Enumerable
            .Range(0, attachmentCount)
            .Select(index => new AssertionAttachment
            {
                Path = $"payloads-{index}/payload-{index}.json",
                SerializationType = SerializationType.Json,
                Data = new { Index = index },
            })
            .ToList();
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "multiple-custom-attachments",
                AssertionName = "MultipleCustomAttachmentsAssertion",
                AssertionHook = new AssertionHookMock
                {
                    AssertionAttachments = assertionAttachments,
                },
                SessionDataList = [],
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 10,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        Reporter.WriteTestResults(assertionResult);
        var resultFile = Directory
            .GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly)
            .Single();
        using var resultDocument = JsonDocument.Parse(File.ReadAllText(resultFile));
        var attachments = GetAttachments(resultDocument.RootElement).ToList();
        var attachmentSources = GetAttachmentSources(resultDocument.RootElement).ToList();
        var portableFiles = Directory.GetFiles(
            AllureResultsFolder,
            "*-attachment.json",
            SearchOption.TopDirectoryOnly
        );
        var legacyRoot = Path.Combine(AllureResultsFolder, "AssertionsAttachments");
        var legacyFiles = Directory.Exists(legacyRoot)
            ? Directory.GetFiles(legacyRoot, "*.json", SearchOption.AllDirectories)
            : [];

        Assert.Multiple(() =>
        {
            Assert.That(attachments, Has.Count.EqualTo(attachmentCount));
            Assert.That(attachmentSources, Has.Count.EqualTo(attachmentCount));
            Assert.That(attachmentSources.Distinct().Count(), Is.EqualTo(attachmentCount));
            Assert.That(portableFiles, Has.Length.EqualTo(attachmentCount));
            Assert.That(legacyFiles, Has.Length.EqualTo(attachmentCount));
            AssertAllureAttachmentSourceContract(attachmentSources);
            Assert.That(
                attachments.Select(attachment =>
                    NormalizePathSeparators(attachment.GetProperty("name").GetString()!)
                ),
                Is.EquivalentTo(
                    Enumerable
                        .Range(0, attachmentCount)
                        .Select(index => $"payloads-{index}/payload-{index}.json")
                )
            );
            Assert.That(
                attachments.Select(attachment => attachment.GetProperty("type").GetString()),
                Is.All.EqualTo("application/json")
            );
            foreach (var index in Enumerable.Range(0, attachmentCount))
            {
                Assert.That(
                    portableFiles,
                    Has.Some.Matches<string>(path =>
                        File.ReadAllText(path).Contains($"\"Index\":{index}")
                    )
                );
                Assert.That(
                    legacyFiles,
                    Has.Some.Matches<string>(path =>
                        File.ReadAllText(path).Contains($"\"Index\":{index}")
                    )
                );
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WriteTestResults_WithNoCustomAttachments_WritesResultWithoutAttachments(
        bool useEmptyHook
    )
    {
        Reporter!.SaveSessionData = false;
        Reporter.SaveTemplate = false;
        Reporter.SaveAttachments = true;
        var assertion = new Assertion
        {
            Name = "no-custom-attachments",
            AssertionName = "NoCustomAttachmentsAssertion",
            SessionDataList = [],
            StatusesToReport = null,
        };
        if (useEmptyHook)
            assertion.AssertionHook = new AssertionHookMock { AssertionAttachments = [] };
        var assertionResult = new AssertionResult
        {
            Assertion = assertion,
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 10,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        Assert.DoesNotThrow(() => Reporter.WriteTestResults(assertionResult));
        var resultFile = Directory
            .GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly)
            .Single();
        using var resultDocument = JsonDocument.Parse(File.ReadAllText(resultFile));

        Assert.Multiple(() =>
        {
            Assert.That(GetAttachments(resultDocument.RootElement), Is.Empty);
            Assert.That(
                Directory.GetFiles(
                    AllureResultsFolder,
                    "*-attachment*",
                    SearchOption.TopDirectoryOnly
                ),
                Is.Empty
            );
            Assert.That(
                Directory.Exists(Path.Combine(AllureResultsFolder, "AssertionsAttachments")),
                Is.False
            );
        });
    }

    [Test]
    public void WriteTestResults_WithRepeatedLogicalAttachment_PreservesFirstWriterContent()
    {
        Reporter!.SaveSessionData = false;
        Reporter.SaveTemplate = false;
        Reporter.SaveAttachments = true;

        AssertionResult CreateResult(int value) =>
            new()
            {
                Assertion = new Assertion
                {
                    Name = "repeated-logical-attachment",
                    AssertionName = "RepeatedLogicalAttachmentAssertion",
                    AssertionHook = new AssertionHookMock
                    {
                        AssertionAttachments =
                        [
                            new AssertionAttachment
                            {
                                Path = "payload.json",
                                SerializationType = SerializationType.Json,
                                Data = new { Value = value },
                            },
                        ],
                    },
                    SessionDataList = [],
                    StatusesToReport = null,
                },
                AssertionStatus = AssertionStatus.Passed,
                TestDurationMs = 10,
                Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
            };

        Reporter.WriteTestResults(CreateResult(1));
        Reporter.WriteTestResults(CreateResult(2));
        var resultFiles = Directory.GetFiles(
            AllureResultsFolder,
            "*-result.json",
            SearchOption.TopDirectoryOnly
        );
        var attachmentSources = resultFiles
            .SelectMany(resultFile =>
            {
                using var document = JsonDocument.Parse(File.ReadAllText(resultFile));
                return GetAttachmentSources(document.RootElement).ToList();
            })
            .ToList();
        var portableAttachment = Directory
            .GetFiles(AllureResultsFolder, "*-attachment.json", SearchOption.TopDirectoryOnly)
            .Single();
        var legacyAttachment = Directory
            .GetFiles(
                Path.Combine(AllureResultsFolder, "AssertionsAttachments"),
                "payload.json",
                SearchOption.AllDirectories
            )
            .Single();

        Assert.Multiple(() =>
        {
            Assert.That(resultFiles, Has.Length.EqualTo(2));
            Assert.That(attachmentSources, Has.Count.EqualTo(2));
            Assert.That(attachmentSources.Distinct().Count(), Is.EqualTo(1));
            Assert.That(File.ReadAllText(portableAttachment), Does.Contain("\"Value\":1"));
            Assert.That(File.ReadAllText(portableAttachment), Does.Not.Contain("\"Value\":2"));
            Assert.That(File.ReadAllText(legacyAttachment), Does.Contain("\"Value\":1"));
            Assert.That(File.ReadAllText(legacyAttachment), Does.Not.Contain("\"Value\":2"));
        });
    }

    [Test]
    public void WriteTestResults_WithOverriddenAttachmentWriter_InvokesProtectedCompatibilityHook()
    {
        var reporter = new TrackingAllureReporter
        {
            Context = new Context { Logger = Globals.Logger },
            FileSystem = new FileSystem(),
            SaveSessionData = false,
            SaveTemplate = false,
            SaveAttachments = true,
        };
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "overridden-writer",
                AssertionName = "OverriddenWriterAssertion",
                AssertionHook = new AssertionHookMock
                {
                    AssertionAttachments =
                    [
                        new AssertionAttachment
                        {
                            Path = "payload.json",
                            SerializationType = SerializationType.Json,
                            Data = new { Value = 5 },
                        },
                    ],
                },
                SessionDataList = [],
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 10,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        reporter.WriteTestResults(assertionResult);
        var resultFile = Directory
            .GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly)
            .Single();
        using var resultDocument = JsonDocument.Parse(File.ReadAllText(resultFile));
        var attachmentSource = GetAttachmentSources(resultDocument.RootElement).Single();

        Assert.Multiple(() =>
        {
            Assert.That(reporter.SaveCalls, Is.EqualTo(1));
            Assert.That(
                NormalizePathSeparators(reporter.LastAttachmentDirectory!),
                Does.StartWith("AssertionsAttachments/")
            );
            Assert.That(reporter.LastAttachmentFileName, Is.EqualTo("payload.json"));
            AssertAllureAttachmentSourceContract([attachmentSource]);
        });
    }

    [Test]
    public void WriteTestResults_WithRepeatedSessionData_ReusesSinglePhysicalAttachment()
    {
        Reporter!.SaveSessionData = true;
        Reporter.SaveLogs = false;
        Reporter.SaveTemplate = false;
        Reporter.SaveAttachments = false;
        var sessionData = new SessionData
        {
            Name = "repeated-session",
            UtcStartTime = DateTime.UtcNow.AddSeconds(-1),
            UtcEndTime = DateTime.UtcNow,
            SessionFailures = [],
        };
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "repeated-session-assertion",
                AssertionName = "RepeatedSessionAssertion",
                SessionDataList = new List<SessionData>
                {
                    sessionData,
                    sessionData,
                }.ToImmutableList(),
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 10,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        Reporter.WriteTestResults(assertionResult);
        var resultFile = Directory
            .GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly)
            .Single();
        using var resultDocument = JsonDocument.Parse(File.ReadAllText(resultFile));
        var attachmentSources = GetAttachmentSources(resultDocument.RootElement).ToList();
        var physicalAttachments = Directory.GetFiles(
            AllureResultsFolder,
            "*-attachment.json",
            SearchOption.TopDirectoryOnly
        );

        Assert.Multiple(() =>
        {
            AssertAllureAttachmentSourceContract(attachmentSources);
            Assert.That(attachmentSources, Has.Count.EqualTo(2));
            Assert.That(attachmentSources.Distinct().Count(), Is.EqualTo(1));
            Assert.That(physicalAttachments, Has.Length.EqualTo(1));
            Assert.That(
                Path.GetFileName(physicalAttachments.Single()),
                Is.EqualTo(attachmentSources.First())
            );
            Assert.That(
                File.ReadAllText(physicalAttachments.Single()),
                Does.Contain("\"Name\": \"repeated-session\"")
            );
        });
    }

    [Test]
    public async Task WriteTestResults_ConcurrentAssertions_ReusesSharedPhysicalAttachment()
    {
        Reporter!.SaveSessionData = true;
        Reporter.SaveLogs = false;
        Reporter.SaveTemplate = false;
        Reporter.SaveAttachments = false;
        var sessionData = new SessionData
        {
            Name = "concurrent-session",
            UtcStartTime = DateTime.UtcNow.AddSeconds(-1),
            UtcEndTime = DateTime.UtcNow,
            SessionFailures = [],
        };
        AssertionResult BuildAssertionResult(string name) =>
            new()
            {
                Assertion = new Assertion
                {
                    Name = name,
                    AssertionName = "ConcurrentAssertion",
                    SessionDataList = new List<SessionData> { sessionData }.ToImmutableList(),
                    StatusesToReport = null,
                },
                AssertionStatus = AssertionStatus.Passed,
                TestDurationMs = 10,
                Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
            };

        using var startBarrier = new Barrier(2);
        var writes = new[] { "concurrent-assertion-a", "concurrent-assertion-b" }
            .Select(name =>
                Task.Run(() =>
                {
                    startBarrier.SignalAndWait();
                    Reporter.WriteTestResults(BuildAssertionResult(name));
                })
            )
            .ToArray();
        await Task.WhenAll(writes);

        var attachmentSources = new List<string>();
        foreach (
            var resultFile in Directory.GetFiles(
                AllureResultsFolder,
                "*-result.json",
                SearchOption.TopDirectoryOnly
            )
        )
        {
            using var resultDocument = JsonDocument.Parse(File.ReadAllText(resultFile));
            attachmentSources.AddRange(GetAttachmentSources(resultDocument.RootElement));
        }

        Assert.Multiple(() =>
        {
            AssertAllureAttachmentSourceContract(attachmentSources);
            Assert.That(attachmentSources, Has.Count.EqualTo(2));
            Assert.That(attachmentSources.Distinct().Count(), Is.EqualTo(1));
            Assert.That(
                Directory.GetFiles(
                    AllureResultsFolder,
                    "*-attachment.json",
                    SearchOption.TopDirectoryOnly
                ),
                Has.Length.EqualTo(1)
            );
        });
    }

    [Test]
    public void WriteTestResults_AttachmentWriteFails_RemovesCacheEntryAndAllowsRetry()
    {
        var realFileSystem = new FileSystem();
        var file = new Mock<IFile>();
        var flatWriteAttempts = 0;
        var resultsDirectory = Path.GetFullPath(AllureResultsFolder);
        file.Setup(fileSystem => fileSystem.WriteAllBytes(It.IsAny<string>(), It.IsAny<byte[]>()))
            .Callback(
                (string path, byte[] content) =>
                {
                    var isPortableAttachment =
                        Path.GetDirectoryName(Path.GetFullPath(path)) == resultsDirectory
                        && Path.GetFileName(path).Contains(AllureConstants.ATTACHMENT_FILE_SUFFIX);
                    if (isPortableAttachment && Interlocked.Increment(ref flatWriteAttempts) == 1)
                        throw new IOException("simulated attachment write failure");
                    realFileSystem.File.WriteAllBytes(path, content);
                }
            );
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.SetupGet(candidate => candidate.Directory).Returns(realFileSystem.Directory);
        fileSystem.SetupGet(candidate => candidate.File).Returns(file.Object);

        Reporter!.FileSystem = fileSystem.Object;
        Reporter.SaveSessionData = true;
        Reporter.SaveLogs = false;
        Reporter.SaveTemplate = false;
        Reporter.SaveAttachments = false;
        var sessionData = new SessionData
        {
            Name = "retry-session",
            UtcStartTime = DateTime.UtcNow.AddSeconds(-1),
            UtcEndTime = DateTime.UtcNow,
            SessionFailures = [],
        };
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "retry-assertion",
                AssertionName = "RetryAssertion",
                SessionDataList = new List<SessionData> { sessionData }.ToImmutableList(),
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 10,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        Assert.Throws<IOException>(() => Reporter.WriteTestResults(assertionResult));
        Assert.DoesNotThrow(() => Reporter.WriteTestResults(assertionResult));

        var resultFile = Directory
            .GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly)
            .Single();
        using var resultDocument = JsonDocument.Parse(File.ReadAllText(resultFile));
        var attachmentSource = GetAttachmentSources(resultDocument.RootElement).Single();
        Assert.Multiple(() =>
        {
            Assert.That(flatWriteAttempts, Is.EqualTo(2));
            AssertAllureAttachmentSourceContract([attachmentSource]);
            Assert.That(
                File.ReadAllText(Path.Combine(AllureResultsFolder, attachmentSource)),
                Does.Contain("\"Name\": \"retry-session\"")
            );
        });
    }

    [Test]
    public void WriteTestResults_WithHostileLongCustomExtension_UsesSafeMediaTypeExtension()
    {
        Reporter!.SaveSessionData = false;
        Reporter.SaveTemplate = false;
        Reporter.SaveAttachments = true;
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "unsupported-extension",
                AssertionName = "UnsupportedExtensionAssertion",
                AssertionHook = new AssertionHookMock
                {
                    AssertionAttachments =
                    [
                        new AssertionAttachment
                        {
                            Path = $"payload.{new string('x', 120)}!",
                            SerializationType = SerializationType.Json,
                            Data = new { Value = 5 },
                        },
                    ],
                },
                SessionDataList = [],
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 10,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        Reporter.WriteTestResults(assertionResult);
        var resultFile = Directory
            .GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly)
            .Single();
        using var resultDocument = JsonDocument.Parse(File.ReadAllText(resultFile));
        var attachmentSource = GetAttachmentSources(resultDocument.RootElement).Single();

        Assert.Multiple(() =>
        {
            AssertAllureAttachmentSourceContract([attachmentSource]);
            Assert.That(attachmentSource, Does.EndWith("-attachment.json"));
        });
    }

    [Test]
    public void WriteTestResults_WithDuplicateNormalizedCustomAttachmentPaths_ThrowsInvalidOperationException()
    {
        Reporter!.SaveSessionData = false;
        Reporter.SaveTemplate = false;
        Reporter.SaveAttachments = true;
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                AssertionHook = new AssertionHookMock
                {
                    AssertionAttachments =
                    [
                        new AssertionAttachment
                        {
                            Path = "folder/file.txt",
                            SerializationType = SerializationType.Json,
                            Data = new byte[] { 0x01 },
                        },
                        new AssertionAttachment
                        {
                            Path = "folder\\file.txt",
                            SerializationType = SerializationType.Json,
                            Data = new byte[] { 0x02 },
                        },
                    ],
                },
                Name = "duplicate-attachments",
                AssertionName = "DuplicateAttachmentAssertion",
                SessionDataList = [],
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        Assert.Throws<InvalidOperationException>(() => Reporter.WriteTestResults(assertionResult));
    }

    [Test]
    public void WriteTestResults_WithMissingCustomAttachmentFileName_ThrowsInvalidOperationException()
    {
        Reporter!.SaveSessionData = false;
        Reporter.SaveTemplate = false;
        Reporter.SaveAttachments = true;
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                AssertionHook = new AssertionHookMock
                {
                    AssertionAttachments =
                    [
                        new AssertionAttachment
                        {
                            Path = string.Empty,
                            SerializationType = SerializationType.Json,
                            Data = new byte[] { 0x01 },
                        },
                    ],
                },
                Name = "missing-file-name",
                AssertionName = "MissingFileNameAssertion",
                SessionDataList = [],
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        Assert.Throws<InvalidOperationException>(() => Reporter.WriteTestResults(assertionResult));
    }

    [Test]
    public void WriteTestResults_WithCoverage_FiltersAndCopiesItToAllureCompatibleAttachmentSource()
    {
        Reporter!.SaveSessionData = false;
        Reporter.SaveTemplate = false;
        Reporter.SaveAttachments = false;
        Reporter!.Context = new Context
        {
            Logger = Globals.Logger,
            ExecutionId = "exec-1",
            CaseName = "case-a",
        };
        var coverageDirectory = Path.Combine(AllureResultsFolder, "Coverages");
        Directory.CreateDirectory(coverageDirectory);
        File.WriteAllText(Path.Combine(coverageDirectory, "exec-1-case-a-session-a.xml"), "match");
        File.WriteAllText(
            Path.Combine(coverageDirectory, "exec-1-case-b-session-a.xml"),
            "wrong-case"
        );
        File.WriteAllText(
            Path.Combine(coverageDirectory, "exec-2-case-a-session-a.xml"),
            "wrong-execution"
        );
        File.WriteAllText(
            Path.Combine(coverageDirectory, "exec-1-case-a-session-b.xml"),
            "wrong-session"
        );

        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "coverage-assertion",
                AssertionName = "CoverageAssertion",
                SessionDataList = new List<SessionData>
                {
                    new()
                    {
                        Name = "session-a",
                        UtcStartTime = DateTime.UtcNow.AddSeconds(-1),
                        UtcEndTime = DateTime.UtcNow,
                        SessionFailures = [],
                    },
                }.ToImmutableList(),
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        Reporter.WriteTestResults(assertionResult);
        var resultFile = Directory
            .GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly)
            .Single();
        using var resultDocument = JsonDocument.Parse(File.ReadAllText(resultFile));
        var attachment = resultDocument
            .RootElement.GetProperty("attachments")
            .EnumerateArray()
            .Single();
        var attachmentSource = attachment.GetProperty("source").GetString()!;
        var attachmentFile = Path.Combine(AllureResultsFolder, attachmentSource);

        Assert.Multiple(() =>
        {
            Assert.That(
                attachment.GetProperty("name").GetString(),
                Is.EqualTo("exec-1-case-a-session-a.xml")
            );
            Assert.That(attachment.GetProperty("type").GetString(), Is.EqualTo("application/xml"));
            AssertAllureAttachmentSourceContract([attachmentSource]);
            Assert.That(File.ReadAllText(attachmentFile), Is.EqualTo("match"));
            Assert.That(Directory.GetFiles(coverageDirectory), Has.Length.EqualTo(4));
        });
    }

    [TestCase(AssertionStatus.Passed, "hook-message", "hook-trace")]
    [TestCase(AssertionStatus.Failed, "hook-message", "hook-trace")]
    [TestCase(AssertionStatus.Unknown, "hook-message", "hook-trace")]
    [TestCase(AssertionStatus.Skipped, "hook-message", "hook-trace")]
    public void GetStatusDetailsAccordingToStatus_ForNonBrokenStatuses_UsesAssertionHookDetails(
        AssertionStatus status,
        string expectedMessage,
        string expectedTrace
    )
    {
        Reporter!.DisplayTrace = true;
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                AssertionHook = new AssertionHookMock
                {
                    AssertionMessage = expectedMessage,
                    AssertionTrace = expectedTrace,
                },
                Name = "status-assertion",
                AssertionName = "StatusAssertion",
                StatusesToReport = null,
            },
            AssertionStatus = status,
            Flaky = new Flaky { IsFlaky = true, FlakinessReasons = [] },
        };

        var method = Reporter
            .GetType()
            .GetMethod(
                "GetStatusDetailsAccordingToStatus",
                BindingFlags.NonPublic | BindingFlags.Instance
            )!;
        var statusDetails = (StatusDetails)method.Invoke(Reporter, [assertionResult])!;

        Assert.Multiple(() =>
        {
            Assert.That(statusDetails.message, Is.EqualTo(expectedMessage));
            Assert.That(statusDetails.trace, Is.EqualTo(expectedTrace));
            Assert.That(statusDetails.flaky, Is.True);
        });
    }

    [Test]
    public void GetStatusDetailsAccordingToStatus_ForBrokenStatus_UsesExceptionDetailsAndHonorsDisplayTraceFlag()
    {
        Reporter!.DisplayTrace = false;
        var exception = new InvalidOperationException("broken-message");
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                AssertionHook = new AssertionHookMock
                {
                    AssertionMessage = "hook-message",
                    AssertionTrace = "hook-trace",
                },
                Name = "broken-assertion",
                AssertionName = "BrokenAssertion",
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Broken,
            BrokenAssertionException = exception,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        var method = Reporter
            .GetType()
            .GetMethod(
                "GetStatusDetailsAccordingToStatus",
                BindingFlags.NonPublic | BindingFlags.Instance
            )!;
        var statusDetails = (StatusDetails)method.Invoke(Reporter, [assertionResult])!;

        Assert.Multiple(() =>
        {
            Assert.That(statusDetails.message, Is.EqualTo("broken-message"));
            Assert.That(
                statusDetails.trace,
                Is.EqualTo("Assertion configured to not display assertion trace")
            );
            Assert.That(statusDetails.flaky, Is.False);
        });
    }

    [Test]
    public void WriteTestResults_WithFailures_CreatesFailureSubStep()
    {
        Reporter!.SaveSessionData = false;
        Reporter.SaveTemplate = false;
        Reporter.SaveAttachments = false;
        var sessionData = new SessionData
        {
            Name = "failed-session",
            UtcStartTime = DateTime.UtcNow.AddSeconds(-2),
            UtcEndTime = DateTime.UtcNow,
            SessionFailures =
            [
                new ActionFailure
                {
                    Name = "action-name",
                    Action = "publish",
                    ActionType = "Publisher",
                    Reason = new Reason { Message = "message", Description = "description" },
                },
            ],
        };
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "failed-assertion",
                AssertionName = "FailedAssertion",
                SessionDataList = new List<SessionData> { sessionData }.ToImmutableList(),
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Failed,
            TestDurationMs = 10,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        Reporter.WriteTestResults(assertionResult);
        var resultFile = Directory
            .GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly)
            .Single();
        var contents = File.ReadAllText(resultFile);

        Assert.Multiple(() =>
        {
            Assert.That(contents, Does.Contain(nameof(sessionData.SessionFailures)));
            Assert.That(contents, Does.Contain("action-name"));
            Assert.That(
                Directory.GetFiles(
                    AllureResultsFolder,
                    "*-attachment*",
                    SearchOption.TopDirectoryOnly
                ),
                Is.Empty
            );
        });
    }

    [Test]
    public void WriteTestResults_WithRepeatedFailures_DeduplicatesAndOrdersFailureSteps()
    {
        Reporter!.SaveSessionData = false;
        Reporter.SaveLogs = false;
        Reporter.SaveTemplate = false;
        Reporter.SaveAttachments = false;
        var repeatedFailure = new ActionFailure
        {
            Name = "action-name",
            Action = "consume",
            ActionType = "ChunkConsumer",
            Reason = new Reason
            {
                Message = "zeta failure",
                Description = "compact zeta description",
            },
        };
        var sessionData = new SessionData
        {
            Name = "failed-session",
            UtcStartTime = DateTime.UtcNow.AddSeconds(-2),
            UtcEndTime = DateTime.UtcNow,
            SessionFailures =
            [
                repeatedFailure,
                repeatedFailure with
                {
                    Reason = new Reason
                    {
                        Message = "alpha failure",
                        Description = "compact alpha description",
                    },
                },
                repeatedFailure,
            ],
        };
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "deduplicated-failure-assertion",
                AssertionName = "FailureAssertion",
                SessionDataList = new List<SessionData> { sessionData }.ToImmutableList(),
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Failed,
            TestDurationMs = 10,
            Flaky = new Flaky
            {
                IsFlaky = true,
                FlakinessReasons =
                [
                    new KeyValuePair<string, List<ActionFailure>>(
                        sessionData.Name,
                        sessionData.SessionFailures
                    ),
                ],
            },
        };

        Reporter.WriteTestResults(assertionResult);
        var resultFile = Directory
            .GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly)
            .Single();
        using var resultDocument = JsonDocument.Parse(File.ReadAllText(resultFile));
        var failureSteps = resultDocument
            .RootElement.GetProperty("steps")[0]
            .GetProperty("steps")[0]
            .GetProperty("steps")
            .EnumerateArray()
            .ToList();

        var messages = failureSteps
            .Select(step => GetAllureParameter(step, nameof(Reason.Message)))
            .ToList();
        var descriptions = failureSteps
            .Select(step => GetAllureParameter(step, nameof(Reason.Description)))
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(failureSteps, Has.Count.EqualTo(2));
            Assert.That(messages, Is.EqualTo(new[] { "alpha failure", "zeta failure" }));
            Assert.That(
                descriptions,
                Is.EqualTo(new[] { "compact alpha description", "compact zeta description" })
            );
        });
    }

    [Test]
    public void AddTestCaseLabelsIfIsPartOfTestCase_AddsSuiteLabelsOnlyWhenCaseExists()
    {
        var method = Reporter!
            .GetType()
            .GetMethod(
                "AddTestCaseLabelsIfIsPartOfTestCase",
                BindingFlags.NonPublic | BindingFlags.Instance
            )!;
        var existingLabels = new List<Label> { Label.Tag("existing") };

        Reporter.Context = new Context { Logger = Globals.Logger };
        var unchanged = (List<Label>)method.Invoke(Reporter, [existingLabels])!;

        Reporter.Context = new Context { Logger = Globals.Logger, CaseName = "case-a" };
        var updated = (List<Label>)method.Invoke(Reporter, [existingLabels])!;

        Assert.Multiple(() =>
        {
            Assert.That(unchanged, Is.SameAs(existingLabels));
            Assert.That(updated.Select(label => label.name).ToList(), Has.Count.EqualTo(3));
            Assert.That(updated.Select(label => label.value).ToList(), Contains.Item("case-a"));
        });
    }

    [Test]
    public void AddExecutionIdLabelsIfIsUnderAnExecutionId_AddsParentSuiteLabelsOnlyWhenExecutionExists()
    {
        var method = Reporter!
            .GetType()
            .GetMethod(
                "AddExecutionIdLabelsIfIsUnderAnExecutionId",
                BindingFlags.NonPublic | BindingFlags.Instance
            )!;
        var existingLabels = new List<Label> { Label.Tag("existing") };

        Reporter.Context = new Context { Logger = Globals.Logger };
        var unchanged = (List<Label>)method.Invoke(Reporter, [existingLabels])!;

        Reporter.Context = new Context { Logger = Globals.Logger, ExecutionId = "exec-a" };
        var updated = (List<Label>)method.Invoke(Reporter, [existingLabels])!;

        Assert.Multiple(() =>
        {
            Assert.That(unchanged, Is.SameAs(existingLabels));
            Assert.That(updated.Select(label => label.name).ToList(), Has.Count.EqualTo(3));
            Assert.That(updated.Select(label => label.value).ToList(), Contains.Item("exec-a"));
        });
    }

    [Test]
    public void GetStatusDetailsAccordingToStatus_WithUnknownEnumValue_ThrowsArgumentOutOfRangeException()
    {
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "invalid-status",
                AssertionName = "InvalidStatusAssertion",
                AssertionHook = new AssertionHookMock(),
                StatusesToReport = null,
            },
            AssertionStatus = (AssertionStatus)999,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        var method = Reporter!
            .GetType()
            .GetMethod(
                "GetStatusDetailsAccordingToStatus",
                BindingFlags.NonPublic | BindingFlags.Instance
            )!;

        var exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(Reporter, [assertionResult])
        );
        Assert.That(exception!.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void GetStatusDetailsAccordingToStatus_ForBrokenStatusWithDisplayTraceEnabled_UsesExceptionTrace()
    {
        Reporter!.DisplayTrace = true;
        var exception = new InvalidOperationException("broken-message");
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "broken-with-trace",
                AssertionName = "BrokenWithTraceAssertion",
                AssertionHook = new AssertionHookMock(),
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Broken,
            BrokenAssertionException = exception,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] },
        };

        var statusDetails = (StatusDetails)
            Reporter
                .GetType()
                .GetMethod(
                    "GetStatusDetailsAccordingToStatus",
                    BindingFlags.NonPublic | BindingFlags.Instance
                )!
                .Invoke(Reporter, [assertionResult])!;

        Assert.That(statusDetails.trace, Does.Contain("System.InvalidOperationException"));
    }

    [Test]
    public void WriteTestResults_WithLinksAndFlakinessReasons_WritesThemIntoAllureResult()
    {
        Reporter!.SaveAttachments = false;
        Reporter.SaveTemplate = false;
        Reporter.SaveSessionData = false;
        Directory.CreateDirectory(AllureResultsFolder);
        Reporter.Context = new Context
        {
            Logger = Globals.Logger,
            RootConfiguration = new ConfigurationBuilder().Build(),
        };
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "linked-assertion",
                AssertionName = "LinkedAssertion",
                AssertionConfiguration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["enabled"] = "true" })
                    .Build(),
                SessionDataList = [],
                StatusesToReport = null,
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 5,
            Flaky = new Flaky
            {
                IsFlaky = true,
                FlakinessReasons =
                [
                    new KeyValuePair<string, List<ActionFailure>>(
                        "session-a",
                        [
                            new ActionFailure
                            {
                                Action = "Publish",
                                ActionType = "Publisher",
                                Name = "publish-step",
                                Reason = new Reason
                                {
                                    Message = "failed intermittently",
                                    Description = "temporary issue",
                                },
                            },
                        ]
                    ),
                ],
            },
            Links = new Dictionary<string, string> { ["Grafana"] = "https://grafana.local/d/123" },
        };

        Reporter.WriteTestResults(assertionResult);

        var resultFile = Directory
            .GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly)
            .Single();
        var contents = File.ReadAllText(resultFile);

        Assert.Multiple(() =>
        {
            Assert.That(contents, Does.Contain("https://grafana.local/d/123"));
            Assert.That(contents, Does.Contain("Flakiness Reasons"));
            Assert.That(contents, Does.Contain("publish-step"));
        });
    }

    [Test]
    public void CreateSessionStep_WithNoFailuresOrArtifacts_ReturnsPassedStepWithoutNestedSteps()
    {
        Reporter!.SaveSessionData = false;
        Reporter.SaveLogs = false;
        var sessionData = new SessionData
        {
            Name = "clean-session",
            UtcStartTime = DateTime.UtcNow.AddSeconds(-1),
            UtcEndTime = DateTime.UtcNow,
            SessionFailures = [],
        };
        var assertion = new Assertion
        {
            Name = "clean-assertion",
            AssertionName = "CleanAssertion",
            StatusesToReport = null,
        };

        var step = (StepResult)
            Reporter
                .GetType()
                .GetMethod("CreateSessionStep", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(Reporter, [sessionData, assertion])!;

        Assert.Multiple(() =>
        {
            Assert.That(step.status, Is.EqualTo(Status.passed));
            Assert.That(step.attachments, Is.Null);
            Assert.That(step.steps, Is.Null);
        });
    }

    private sealed class TrackingAllureReporter : AllureReporter
    {
        public int SaveCalls { get; private set; }
        public string? LastAttachmentDirectory { get; private set; }
        public string? LastAttachmentFileName { get; private set; }

        protected override void SaveAttachmentIfNotAlreadySaved(
            byte[] attachmentContent,
            string attachmentDirectory,
            string attachmentFileName
        )
        {
            SaveCalls++;
            LastAttachmentDirectory = attachmentDirectory;
            LastAttachmentFileName = attachmentFileName;
            base.SaveAttachmentIfNotAlreadySaved(
                attachmentContent,
                attachmentDirectory,
                attachmentFileName
            );
        }
    }
}
