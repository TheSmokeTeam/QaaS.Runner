using System.Collections.Generic;
using System.Collections.Immutable;
using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;
using Allure.Commons;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.Tests.Mocks;
using QaaS.Runner.Infrastructure;

namespace QaaS.Runner.Assertions.Tests.AssertionResultsWritersTests;

[TestFixture]
public class AllureReporterTests
{
    private sealed class ExposedAllureReporter : AllureReporter
    {
        public void SaveAttachment(byte[] attachmentContent, string attachmentDirectory, string attachmentFileName)
        {
            SaveAttachmentIfNotAlreadySaved(attachmentContent, attachmentDirectory, attachmentFileName);
        }
    }

    [SetUp]
    public void SetUp()
    {
        if (FileSystem.Directory.Exists(AllureResultsFolder))
            FileSystem.Directory.Delete(AllureResultsFolder, true);
        // Setup reporter with mocked dependencies
        Reporter = new AllureReporter
        {
            Context = new Context
            {
                Logger = Globals.Logger
            },
            SaveLogs = true,
            SaveAttachments = true,
            FileSystem = new FileSystem(),
            Name = null
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


    private static IEnumerable<TestCaseData> TestWriteTestResultsEnumerableCaseSource()
    {
        yield return new TestCaseData(new Context[] { new() { Logger = Globals.Logger } },
            new AssertionResult
            {
                Assertion = new Assertion
                {
                    Name = "Test",
                    AssertionName = "AssertionOne",
                    SessionDataList =
                        [],
                    AssertionHook = null,
                    StatussesToReport = null
                },
                AssertionStatus = AssertionStatus.Passed,
                TestDurationMs = 10,
                Flaky = new Flaky
                {
                    IsFlaky = false,
                    FlakinessReasons = new List<KeyValuePair<string, List<ActionFailure>>>()
                }
            }, false, true, 2).SetName("NoSessionData");

        yield return new TestCaseData(new Context[] { new() { Logger = Globals.Logger } },
            new AssertionResult
            {
                Assertion = new Assertion
                {
                    Name = "Test",
                    AssertionName = "AssertionOne",
                    SessionDataList = new List<SessionData>
                    {
                        new()
                        {
                            Name = "test",
                            SessionFailures = new List<ActionFailure>()
                        }
                    }.ToImmutableList(),
                    AssertionHook = null,
                    StatussesToReport = null
                },

                AssertionStatus = AssertionStatus.Passed,
                TestDurationMs = 10,
                Flaky = new Flaky
                {
                    IsFlaky = false,
                    FlakinessReasons = new List<KeyValuePair<string, List<ActionFailure>>>()
                }
            }, true, false, 2).SetName("WithOneSessionData");

        yield return new TestCaseData(
            new Context[]
            {
                new() { Logger = Globals.Logger, CaseName = "test" },
                new() { Logger = Globals.Logger, CaseName = "test2" }
            },
            new AssertionResult
            {
                Assertion = new Assertion
                {
                    Name = "Test",
                    AssertionName = "AssertionOne",
                    SessionDataList = new List<SessionData>
                    {
                        new()
                        {
                            Name = "test",
                            SessionFailures = new List<ActionFailure>()
                        }
                    }.ToImmutableList(),
                    AssertionHook = null,
                    StatussesToReport = null
                },
                AssertionStatus = AssertionStatus.Passed,
                TestDurationMs = 10,
                Flaky = new Flaky
                {
                    IsFlaky = false,
                    FlakinessReasons = new List<KeyValuePair<string, List<ActionFailure>>>()
                }
            }, true, true, 6).SetName("MultipleCasesWithOneSameSessionData");

        yield return new TestCaseData(
            new Context[]
            {
                new() { Logger = Globals.Logger, ExecutionId = "test", CaseName = "a" },
                new() { Logger = Globals.Logger, ExecutionId = "test2", CaseName = "a" }
            },
            new AssertionResult
            {
                Assertion = new Assertion
                {
                    Name = "Test",
                    AssertionName = "AssertionOne",
                    SessionDataList = new List<SessionData>
                    {
                        new()
                        {
                            Name = "test",
                            SessionFailures = new List<ActionFailure>()
                        }
                    }.ToImmutableList(),
                    AssertionHook = null,
                    StatussesToReport = null
                },
                AssertionStatus = AssertionStatus.Passed,
                TestDurationMs = 10,
                Flaky = new Flaky
                {
                    IsFlaky = false,
                    FlakinessReasons = new List<KeyValuePair<string, List<ActionFailure>>>()
                }
            }, true, false, 4).SetName("MultipleExecutionsSameCasesWithOneSameSessionData");

        yield return new TestCaseData(new Context[] { new() { Logger = Globals.Logger } },
            new AssertionResult
            {
                Assertion = new Assertion
                {
                    Name = "Test",
                    AssertionName = "AssertionOne",
                    SessionDataList = new List<SessionData>
                    {
                        new()
                        {
                            Name = "test",
                            SessionFailures = new List<ActionFailure>()
                        },
                        new()
                        {
                            Name = "test2",
                            SessionFailures = new List<ActionFailure>()
                        },
                        new()
                        {
                            Name = "test3",
                            SessionFailures = new List<ActionFailure>()
                        }
                    }.ToImmutableList(),
                    AssertionHook = null,
                    StatussesToReport = null
                },
                AssertionStatus = AssertionStatus.Passed,
                TestDurationMs = 10,
                Flaky = new Flaky
                {
                    IsFlaky = false,
                    FlakinessReasons = new List<KeyValuePair<string, List<ActionFailure>>>()
                }
            }, true, false, 4).SetName("WithMultipleSessionData");


        yield return new TestCaseData(new Context[] { new() { Logger = Globals.Logger } },
            new AssertionResult
            {
                Assertion = new Assertion
                {
                    Name = "Test",
                    AssertionName = "AssertionOne",
                    SessionDataList = new List<SessionData>
                    {
                        new()
                        {
                            Name = "test",
                            SessionFailures = new List<ActionFailure>()
                        },
                        new()
                        {
                            Name = "test2",
                            SessionFailures = new List<ActionFailure>()
                        },
                        new()
                        {
                            Name = "test3",
                            SessionFailures = new List<ActionFailure>()
                        },
                        new()
                        {
                            Name = "test",
                            SessionFailures = new List<ActionFailure>()
                        },
                        new()
                        {
                            Name = "test2",
                            SessionFailures = new List<ActionFailure>()
                        },
                        new()
                        {
                            Name = "test3",
                            SessionFailures = new List<ActionFailure>()
                        }
                    }.ToImmutableList(),
                    AssertionHook = null,
                    StatussesToReport = null
                },
                AssertionStatus = AssertionStatus.Passed,
                TestDurationMs = 10,
                Flaky = new Flaky
                {
                    IsFlaky = false,
                    FlakinessReasons = new List<KeyValuePair<string, List<ActionFailure>>>()
                }
            }, true, true, 5).SetName("WithMultipleSessionDataDuplicated");
    }

    [Test]
    [TestCaseSource(nameof(TestWriteTestResultsEnumerableCaseSource))]
    [Order(1)]
    public void
        TestWriteTestResults_ForEachContextCallFunctionWithAssertionResult_ShouldWriteExpectedNumberOfAttachmentsForIt(
            IEnumerable<Context> contexts, AssertionResult assertionResult, bool saveSessionData, bool saveTemplate,
            int expectedItemCount)
    {
        // Arrange
        List<AllureReporter> allureResultsHandlers = [];
        foreach (var context in contexts)
            allureResultsHandlers.Add(new AllureReporter
            {
                Context = context,
                SaveAttachments = false,
                SaveTemplate = saveTemplate,
                SaveSessionData = saveSessionData,
                FileSystem = new FileSystem(),
                Name = null
            });

        // Act
        foreach (var allureResultsHandler in allureResultsHandlers)
            allureResultsHandler.WriteTestResults(assertionResult);

        // Assert
        Assert.AreEqual(expectedItemCount,
            FileSystem.Directory.GetFiles(AllureResultsFolder,
                "", SearchOption.AllDirectories).Length);
    }

    [Test]
    [TestCase("BaseDirectory", "SpecificAssertion", "Run", "Case1")]
    [TestCase("BaseDirectory", "SpecificAssertion", "Run", null)]
    [TestCase("AssertionDirectory", null, null, "Case3")]
    [TestCase("AssertionDirectory")]
    public void
        TestGetAttachmentDirectory_CallFunctionWithBaseAndExtraDirectoriesWithDifferentContexts_ShouldReturnCorrectAttachmentDirectory(
            string baseAttachmentDirectoryInsideAllureDirectory,
            string? extraSubDirectoryName = null, string? executionId = null, string? caseName = null)
    {
        var context = new Context
        {
            Logger = Globals.Logger,
            ExecutionId = executionId,
            CaseName = caseName
        };
        Reporter?.Context = context;
        // Arrange
        var getAttachmentDirectoryMethod = Reporter?.GetType()
            .GetMethod("GetAttachmentDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var epochTestSuiteStartTimeField = typeof(AllureReporter)
            .GetProperty("EpochTestSuiteStartTime", BindingFlags.Public | BindingFlags.Instance)!;

        // Act
        var attachmentDirectory = (string)getAttachmentDirectoryMethod.Invoke(Reporter,
            [baseAttachmentDirectoryInsideAllureDirectory, extraSubDirectoryName])!;
        var expectedAttachmentDirectory = Path.Join(baseAttachmentDirectoryInsideAllureDirectory,
            epochTestSuiteStartTimeField.GetValue(Reporter)!.ToString(),
            FileSystemExtensions.MakeValidDirectoryName(executionId),
            FileSystemExtensions.MakeValidDirectoryName(caseName),
            FileSystemExtensions.MakeValidDirectoryName(extraSubDirectoryName));
        // Assert
        Assert.AreEqual(expectedAttachmentDirectory, attachmentDirectory);
    }

    [Test]
    public void GetAttachmentDirectory_WithNullBaseDirectory_ThrowsInvalidOperationException()
    {
        var method = Reporter!.GetType()
            .GetMethod("GetAttachmentDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(Reporter, new object?[] { null, null }));

        Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(5)]
    [TestCase(10)]
    public void TestSaveAssertionAttachmentsToAllure_CallFunctionWithXAttachmentsToSave_FunctionReturnXAttachments(
        int numberOfAttachments)
    {
        // Arrange
        var attachments = new List<AssertionAttachment>();
        for (var i = 0; i < numberOfAttachments; i++)
        {
            var attachment = new AssertionAttachment
            {
                Path = $"attachment-{i}.txt",
                SerializationType = SerializationType.Json,
                Data = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }
            };
            attachments.Add(attachment);
        }

        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                AssertionHook = new AssertionHookMock
                {
                    AssertionAttachments = attachments
                },
                Name = null,
                AssertionName = null,
                StatussesToReport = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = null
        };

        var saveAssertionAttachmentsToAllureMethod = Reporter?.GetType()
            .GetMethod("SaveAssertionAttachmentsToAllure", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // Act
        var result =
            (List<Attachment>)saveAssertionAttachmentsToAllureMethod.Invoke(Reporter, [assertionResult])!;


        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(numberOfAttachments));

        // Verify that each attachment has the expected properties
        for (var i = 0; i < numberOfAttachments; i++)
        {
            Assert.That(result[i].name, Is.Not.Null.And.Not.Empty);
            Assert.That(result[i].source, Is.Not.Null.And.Not.Empty);
            Assert.That(result[i].type, Is.Not.Null.And.Not.Empty);
        }
    }

    [Test]
    public void SaveAssertionAttachmentsToAllure_WithTraversalPath_ThrowsInvalidOperationException()
    {
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
                            Data = new byte[] { 0x01 }
                        }
                    ]
                },
                Name = "unsafe-assertion",
                AssertionName = "AssertionOne",
                StatussesToReport = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = null
        };

        var saveAssertionAttachmentsToAllureMethod = Reporter?.GetType()
            .GetMethod("SaveAssertionAttachmentsToAllure", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var exception = Assert.Throws<TargetInvocationException>(() =>
            saveAssertionAttachmentsToAllureMethod.Invoke(Reporter, [assertionResult]));

        Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void SaveConfigurationTemplateToAllure_WithRenderedTemplate_UsesStoredTemplateContent()
    {
        const string renderedTemplate = "Sessions:\n  - Name: RabbitRoundTrip\n";
        Reporter!.Context.InsertValueIntoGlobalDictionary(["__RunnerArtifacts", "RenderedTemplate"], renderedTemplate);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sessions:0:Name"] = "incomplete"
            })
            .Build();

        var method = Reporter.GetType()
            .GetMethod("SaveConfigurationTemplateToAllure", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var attachment = (Attachment)method.Invoke(Reporter, [configuration])!;
        var attachmentPath = Path.Combine(AllureResultsFolder, attachment.source);

        Assert.That(File.Exists(attachmentPath), Is.True);
        Assert.That(File.ReadAllText(attachmentPath), Is.EqualTo(renderedTemplate));
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
            SessionFailures = []
        };
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "log-assertion",
                AssertionName = "LogAssertion",
                SessionDataList = new List<SessionData> { sessionData }.ToImmutableList(),
                StatussesToReport = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 10,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] }
        };

        Reporter.WriteTestResults(assertionResult);
        var resultFile = Directory.GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly).Single();
        var logAttachmentPath = Directory.GetFiles(Path.Combine(AllureResultsFolder, "SessionLogs"), "*.log",
            SearchOption.AllDirectories)
            .Single();

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(resultFile), Does.Contain("SessionLog"));
            Assert.That(File.ReadAllText(resultFile), Does.Contain("SessionLogs\\"));
            Assert.That(Directory.GetFiles(AllureResultsFolder, "*-attachment*", SearchOption.TopDirectoryOnly), Is.Empty);
            Assert.That(File.Exists(logAttachmentPath), Is.True);
            Assert.That(File.ReadAllText(logAttachmentPath), Does.Contain("Starting session test-session"));
            Assert.That(File.ReadAllText(logAttachmentPath), Does.Contain("Session test-session completed."));
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
            SessionFailures = []
        };
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "log-assertion",
                AssertionName = "LogAssertion",
                SessionDataList = new List<SessionData> { sessionData }.ToImmutableList(),
                StatussesToReport = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 10,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] }
        };

        Reporter.WriteTestResults(assertionResult);
        var resultFile = Directory.GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly).Single();

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(resultFile), Does.Not.Contain("SessionLog"));
            Assert.That(Directory.Exists(Path.Combine(AllureResultsFolder, "SessionLogs")), Is.False);
            Assert.That(Directory.GetFiles(AllureResultsFolder, "*-attachment*", SearchOption.TopDirectoryOnly), Is.Empty);
        });
    }

    [Test]
    public void WriteTestResults_WithSessionArtifacts_StoresAttachmentsOnlyInsideLegacyFolders()
    {
        Reporter!.SaveSessionData = true;
        Reporter.SaveTemplate = true;
        Reporter.SaveAttachments = false;
        Reporter.Context = new Context
        {
            Logger = Globals.Logger,
            RootConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Sessions:0:Name"] = "RabbitRoundTrip"
                })
                .Build()
        };
        Reporter.Context.AppendSessionLog("test-session", "session-log-entry");
        var sessionData = new SessionData
        {
            Name = "test-session",
            UtcStartTime = DateTime.UtcNow.AddSeconds(-1),
            UtcEndTime = DateTime.UtcNow,
            SessionFailures = []
        };
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "artifact-assertion",
                AssertionName = "ArtifactAssertion",
                SessionDataList = new List<SessionData> { sessionData }.ToImmutableList(),
                StatussesToReport = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 10,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] }
        };

        Reporter.WriteTestResults(assertionResult);
        var resultFile = Directory.GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly).Single();
        var contents = File.ReadAllText(resultFile);
        var sessionsDataCopy = Directory.GetFiles(Path.Combine(AllureResultsFolder, "SessionsData"), "*.json",
            SearchOption.AllDirectories).Single();
        var sessionLogCopy = Directory.GetFiles(Path.Combine(AllureResultsFolder, "SessionLogs"), "*.log",
            SearchOption.AllDirectories).Single();
        var templateCopy = Directory.GetFiles(Path.Combine(AllureResultsFolder, "Templates"), "*.yaml",
            SearchOption.AllDirectories).Single();

        Assert.Multiple(() =>
        {
            Assert.That(contents, Does.Contain("SessionsData\\"));
            Assert.That(contents, Does.Contain("SessionLogs\\"));
            Assert.That(contents, Does.Contain("Templates\\"));
            Assert.That(Directory.GetFiles(AllureResultsFolder, "*-attachment*", SearchOption.TopDirectoryOnly), Is.Empty);
            Assert.That(File.ReadAllText(sessionsDataCopy), Does.Contain("\"Name\": \"test-session\""));
            Assert.That(File.ReadAllText(sessionLogCopy), Does.Contain("session-log-entry"));
            Assert.That(File.ReadAllText(templateCopy), Does.Contain("RabbitRoundTrip"));
        });
    }

    [Test]
    public void WriteTestResults_WithCustomAttachments_StoresAttachmentOnlyInsideAssertionFolders()
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
                            Data = new { Value = 5 }
                        }
                    ]
                },
                SessionDataList = [],
                StatussesToReport = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 10,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] }
        };

        Reporter.WriteTestResults(assertionResult);
        var resultFile = Directory.GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly).Single();
        var legacyAttachmentCopy = Directory.GetFiles(Path.Combine(AllureResultsFolder, "AssertionsAttachments"),
            "payload.json", SearchOption.AllDirectories).Single();
        var contents = File.ReadAllText(resultFile);

        Assert.Multiple(() =>
        {
            Assert.That(contents, Does.Contain("AssertionsAttachments\\"));
            Assert.That(Directory.GetFiles(AllureResultsFolder, "*-attachment*", SearchOption.TopDirectoryOnly), Is.Empty);
            Assert.That(File.ReadAllText(legacyAttachmentCopy), Does.Contain("\"Value\":5"));
        });
    }

    [Test]
    public void SaveAttachmentIfNotAlreadySaved_WhenDuplicateAttachmentIsSaved_WritesFileOnlyOnce()
    {
        var reporter = new ExposedAllureReporter
        {
            Context = new Context { Logger = Globals.Logger },
            FileSystem = new FileSystem()
        };

        reporter.SaveAttachment([0x01], "Attachments", "duplicate.txt");
        reporter.SaveAttachment([0x02], "Attachments", "duplicate.txt");

        var savedFile = Path.Combine(AllureResultsFolder, "Attachments", "duplicate.txt");
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(savedFile), Is.True);
            Assert.That(File.ReadAllBytes(savedFile), Is.EqualTo(new byte[] { 0x01 }));
            Assert.That(Directory.GetFiles(Path.Combine(AllureResultsFolder, "Attachments")),
                Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void SaveAttachmentIfNotAlreadySaved_WithNullFileName_ThrowsInvalidOperationException()
    {
        var reporter = new ExposedAllureReporter
        {
            Context = new Context { Logger = Globals.Logger },
            FileSystem = new FileSystem()
        };

        Assert.Throws<InvalidOperationException>(() => reporter.SaveAttachment([0x01], "Attachments", null!));
    }

    [Test]
    public void SaveSessionLogToAllure_WhenNoLogWasStored_ReturnsNull()
    {
        var method = Reporter!.GetType()
            .GetMethod("SaveSessionLogToAllure", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var attachment = method.Invoke(Reporter, [new SessionData { Name = "missing-log-session" }]);

        Assert.That(attachment, Is.Null);
    }

    [Test]
    public void SaveAssertionAttachmentsToAllure_WhenAssertionHookIsMissing_ReturnsEmptyList()
    {
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "no-hook",
                AssertionName = "NoHookAssertion",
                AssertionHook = null,
                StatussesToReport = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = null
        };

        var attachments = (List<Attachment>)Reporter!.GetType()
            .GetMethod("SaveAssertionAttachmentsToAllure", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(Reporter, [assertionResult])!;

        Assert.That(attachments, Is.Empty);
    }

    [Test]
    public void SaveAssertionAttachmentsToAllure_WithDuplicateNormalizedPaths_ThrowsInvalidOperationException()
    {
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
                            Data = new byte[] { 0x01 }
                        },
                        new AssertionAttachment
                        {
                            Path = "folder\\file.txt",
                            SerializationType = SerializationType.Json,
                            Data = new byte[] { 0x02 }
                        }
                    ]
                },
                Name = "duplicate-attachments",
                AssertionName = "DuplicateAttachmentAssertion",
                StatussesToReport = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = null
        };

        var method = Reporter!.GetType()
            .GetMethod("SaveAssertionAttachmentsToAllure", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(Reporter, [assertionResult]));
        Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void SaveAssertionAttachmentsToAllure_WithMissingFileName_ThrowsInvalidOperationException()
    {
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
                            Data = new byte[] { 0x01 }
                        }
                    ]
                },
                Name = "missing-file-name",
                AssertionName = "MissingFileNameAssertion",
                StatussesToReport = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = null
        };

        var method = Reporter!.GetType()
            .GetMethod("SaveAssertionAttachmentsToAllure", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(Reporter, [assertionResult]));
        Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void GetCoveragesAsAttachments_FiltersByExecutionCaseAndSessionName()
    {
        Reporter!.Context = new Context
        {
            Logger = Globals.Logger,
            ExecutionId = "exec-1",
            CaseName = "case-a"
        };
        var coverageDirectory = Path.Combine(AllureResultsFolder, "Coverages");
        Directory.CreateDirectory(coverageDirectory);
        File.WriteAllText(Path.Combine(coverageDirectory, "exec-1-case-a-session-a.xml"), "match");
        File.WriteAllText(Path.Combine(coverageDirectory, "exec-1-case-b-session-a.xml"), "wrong-case");
        File.WriteAllText(Path.Combine(coverageDirectory, "exec-2-case-a-session-a.xml"), "wrong-execution");
        File.WriteAllText(Path.Combine(coverageDirectory, "exec-1-case-a-session-b.xml"), "wrong-session");

        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "coverage-assertion",
                AssertionName = "CoverageAssertion",
                SessionDataList = new List<SessionData>
                {
                    new() { Name = "session-a", SessionFailures = [] }
                }.ToImmutableList(),
                StatussesToReport = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 0,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] }
        };

        var method = Reporter.GetType()
            .GetMethod("GetCoveragesAsAttachments", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var attachments = (List<Attachment>)method.Invoke(Reporter, [assertionResult])!;

        Assert.That(attachments.Select(attachment => attachment.name),
            Is.EqualTo(new[] { "exec-1-case-a-session-a.xml" }));
    }

    [TestCase(AssertionStatus.Passed, "hook-message", "hook-trace")]
    [TestCase(AssertionStatus.Failed, "hook-message", "hook-trace")]
    [TestCase(AssertionStatus.Unknown, "hook-message", "hook-trace")]
    [TestCase(AssertionStatus.Skipped, "hook-message", "hook-trace")]
    public void GetStatusDetailsAccordingToStatus_ForNonBrokenStatuses_UsesAssertionHookDetails(
        AssertionStatus status, string expectedMessage, string expectedTrace)
    {
        Reporter!.DisplayTrace = true;
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                AssertionHook = new AssertionHookMock
                {
                    AssertionMessage = expectedMessage,
                    AssertionTrace = expectedTrace
                },
                Name = "status-assertion",
                AssertionName = "StatusAssertion",
                StatussesToReport = null
            },
            AssertionStatus = status,
            Flaky = new Flaky { IsFlaky = true, FlakinessReasons = [] }
        };

        var method = Reporter.GetType()
            .GetMethod("GetStatusDetailsAccordingToStatus", BindingFlags.NonPublic | BindingFlags.Instance)!;
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
                    AssertionTrace = "hook-trace"
                },
                Name = "broken-assertion",
                AssertionName = "BrokenAssertion",
                StatussesToReport = null
            },
            AssertionStatus = AssertionStatus.Broken,
            BrokenAssertionException = exception,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] }
        };

        var method = Reporter.GetType()
            .GetMethod("GetStatusDetailsAccordingToStatus", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var statusDetails = (StatusDetails)method.Invoke(Reporter, [assertionResult])!;

        Assert.Multiple(() =>
        {
            Assert.That(statusDetails.message, Is.EqualTo("broken-message"));
            Assert.That(statusDetails.trace, Is.EqualTo("Assertion configured to not display assertion trace"));
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
                    Reason = new Reason
                    {
                        Message = "message",
                        Description = "description"
                    }
                }
            ]
        };
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "failed-assertion",
                AssertionName = "FailedAssertion",
                SessionDataList = new List<SessionData> { sessionData }.ToImmutableList(),
                StatussesToReport = null
            },
            AssertionStatus = AssertionStatus.Failed,
            TestDurationMs = 10,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] }
        };

        Reporter.WriteTestResults(assertionResult);
        var resultFile = Directory.GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly).Single();
        var contents = File.ReadAllText(resultFile);

        Assert.Multiple(() =>
        {
            Assert.That(contents, Does.Contain(nameof(sessionData.SessionFailures)));
            Assert.That(contents, Does.Contain("action-name"));
            Assert.That(Directory.GetFiles(AllureResultsFolder, "*-attachment*", SearchOption.TopDirectoryOnly),
                Is.Empty);
        });
    }

    [Test]
    public void GetAttachmentsForAssertion_WithSaveFlagsDisabledAndNoCoverage_ReturnsEmptyList()
    {
        Reporter!.SaveAttachments = false;
        Reporter.SaveTemplate = false;
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "no-attachments",
                AssertionName = "NoAttachmentsAssertion",
                SessionDataList = [],
                StatussesToReport = null
            },
            AssertionStatus = AssertionStatus.Passed,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] }
        };

        var attachments = (List<Attachment>)Reporter.GetType()
            .GetMethod("GetAttachmentsForAssertion", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(Reporter, [assertionResult])!;

        Assert.That(attachments, Is.Empty);
    }

    [Test]
    public void GetAttachmentsForAssertion_WithTemplateOnly_ReturnsTemplateAttachment()
    {
        Reporter!.SaveAttachments = false;
        Reporter.SaveTemplate = true;
        Reporter.Context = new Context
        {
            Logger = Globals.Logger,
            RootConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MetaData:System"] = "QaaS",
                    ["MetaData:Team"] = "Smoke"
                })
                .Build()
        };
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "template-only",
                AssertionName = "TemplateOnlyAssertion",
                SessionDataList = [],
                StatussesToReport = null
            },
            AssertionStatus = AssertionStatus.Passed,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] }
        };

        var attachments = (List<Attachment>)Reporter.GetType()
            .GetMethod("GetAttachmentsForAssertion", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(Reporter, [assertionResult])!;

        Assert.That(attachments, Has.Count.EqualTo(1));
        Assert.That(attachments[0].source, Does.EndWith("template.yaml"));
        Assert.That(attachments[0].type, Is.EqualTo("application/yaml"));
    }

    [Test]
    public void GetAttachmentsForAssertion_WithCustomAttachmentsOnly_ReturnsCustomAttachment()
    {
        Reporter!.SaveAttachments = true;
        Reporter.SaveTemplate = false;
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
                            Path = "payload.json",
                            SerializationType = SerializationType.Json,
                            Data = new { Value = 5 }
                        }
                    ]
                },
                SessionDataList = [],
                StatussesToReport = null
            },
            AssertionStatus = AssertionStatus.Passed,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] }
        };

        var attachments = (List<Attachment>)Reporter.GetType()
            .GetMethod("GetAttachmentsForAssertion", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(Reporter, [assertionResult])!;

        Assert.That(attachments, Has.Count.EqualTo(1));
        Assert.That(attachments[0].source, Does.EndWith("payload.json"));
        Assert.That(File.Exists(Path.Combine(AllureResultsFolder, attachments[0].source)), Is.True);
    }

    [Test]
    public void SaveDataToAllure_WithNullFileName_ThrowsInvalidOperationException()
    {
        var method = Reporter!.GetType()
            .GetMethod("SaveDataToAllure", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(Reporter, [new byte[] { 0x01 }, null, "Attachments", "attachment", "text/plain"]));

        Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void SaveDataToAllure_WithEmptyAttachmentDirectory_UsesFileNameAsSource()
    {
        var method = Reporter!.GetType()
            .GetMethod("SaveDataToAllure", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var attachment = (Attachment)method.Invoke(Reporter,
            [new byte[] { 0x01 }, "root-file.txt", string.Empty, "root-file", "text/plain"])!;

        Assert.Multiple(() =>
        {
            Assert.That(attachment.source, Is.EqualTo("root-file.txt"));
            Assert.That(File.Exists(Path.Combine(AllureResultsFolder, "root-file.txt")), Is.True);
        });
    }

    [Test]
    public void AddTestCaseLabelsIfIsPartOfTestCase_AddsSuiteLabelsOnlyWhenCaseExists()
    {
        var method = Reporter!.GetType()
            .GetMethod("AddTestCaseLabelsIfIsPartOfTestCase", BindingFlags.NonPublic | BindingFlags.Instance)!;
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
        var method = Reporter!.GetType()
            .GetMethod("AddExecutionIdLabelsIfIsUnderAnExecutionId", BindingFlags.NonPublic | BindingFlags.Instance)!;
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
                StatussesToReport = null
            },
            AssertionStatus = (AssertionStatus)999,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] }
        };

        var method = Reporter!.GetType()
            .GetMethod("GetStatusDetailsAccordingToStatus", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(Reporter, [assertionResult]));
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
                StatussesToReport = null
            },
            AssertionStatus = AssertionStatus.Broken,
            BrokenAssertionException = exception,
            Flaky = new Flaky { IsFlaky = false, FlakinessReasons = [] }
        };

        var statusDetails = (StatusDetails)Reporter.GetType()
            .GetMethod("GetStatusDetailsAccordingToStatus", BindingFlags.NonPublic | BindingFlags.Instance)!
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
            RootConfiguration = new ConfigurationBuilder().Build()
        };
        var assertionResult = new AssertionResult
        {
            Assertion = new Assertion
            {
                Name = "linked-assertion",
                AssertionName = "LinkedAssertion",
                AssertionConfiguration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["enabled"] = "true"
                    })
                    .Build(),
                SessionDataList = [],
                StatussesToReport = null
            },
            AssertionStatus = AssertionStatus.Passed,
            TestDurationMs = 5,
            Flaky = new Flaky
            {
                IsFlaky = true,
                FlakinessReasons =
                [
                    new KeyValuePair<string, List<ActionFailure>>("session-a",
                    [
                        new ActionFailure
                        {
                            Action = "Publish",
                            ActionType = "Publisher",
                            Name = "publish-step",
                            Reason = new Reason
                            {
                                Message = "failed intermittently",
                                Description = "temporary issue"
                            }
                        }
                    ])
                ]
            },
            Links = new Dictionary<string, string>
            {
                ["Grafana"] = "https://grafana.local/d/123"
            }
        };

        Reporter.WriteTestResults(assertionResult);

        var resultFile = Directory.GetFiles(AllureResultsFolder, "*-result.json", SearchOption.TopDirectoryOnly).Single();
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
        var sessionData = new SessionData
        {
            Name = "clean-session",
            UtcStartTime = DateTime.UtcNow.AddSeconds(-1),
            UtcEndTime = DateTime.UtcNow,
            SessionFailures = []
        };

        var step = (StepResult)Reporter.GetType()
            .GetMethod("CreateSessionStep", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(Reporter, [sessionData])!;

        Assert.Multiple(() =>
        {
            Assert.That(step.status, Is.EqualTo(Status.passed));
            Assert.That(step.attachments, Is.Null);
            Assert.That(step.steps, Is.Null);
        });
    }
}
