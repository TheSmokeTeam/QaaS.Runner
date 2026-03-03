using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Abstractions;
using System.Reflection;
using Allure.Commons;
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
    [SetUp]
    public void SetUp()
    {
        if (FileSystem.Directory.Exists(AllureResultsFolder))
            FileSystem.Directory.Delete(AllureResultsFolder, true);
        // Setup reporter with mocked dependencies
        Reporter = new AllureReporter
        {
            Context = new Context { Logger = Globals.Logger },
            SaveAttachments = true,
            FileSystem = new FileSystem()
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
                    SessionDataList = []
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
                    }.ToImmutableList()
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
                    }.ToImmutableList()
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
                    }.ToImmutableList()
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
                    }.ToImmutableList()
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
                    }.ToImmutableList()
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
                FileSystem = new FileSystem()
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
            executionId, FileSystemExtensions.MakeValidDirectoryName(caseName),
            extraSubDirectoryName);
        // Assert
        Assert.AreEqual(expectedAttachmentDirectory, attachmentDirectory);
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
                }
            }
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
}