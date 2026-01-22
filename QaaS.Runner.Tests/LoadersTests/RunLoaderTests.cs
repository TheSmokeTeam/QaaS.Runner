using System.Reflection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Moq;
using QaaS.Framework.Configurations.References;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Runner.Loaders;
using QaaS.Runner.Options;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;

namespace QaaS.Runner.Tests.LoadersTests
{
    [TestFixture]
    public class RunLoaderTests
    {
        private static readonly MethodInfo BuildContextMethodInfo = typeof(RunLoader<Runner, RunOptions>).GetMethod(
            "BuildContext", BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static IEnumerable<TestCaseData> TestBuildContextCaseData()
        {
            // Test Case 1: Basic build context with minimal options
            var basicOptions = new RunOptions
            {
                ConfigurationFile = "test.yaml",
                OverwriteFiles = new List<string>(),
                OverwriteArguments = new List<string>(),
                PushReferences = new List<string>()
            };

            yield return new TestCaseData(basicOptions, null, null)
                .SetName("BasicBuildWithContext");

            // Test Case 2: Build context with execution ID
            yield return new TestCaseData(basicOptions, "execution123", null)
                .SetName("WithExecutionId");

            // Test Case 3: Build context with case file path
            yield return new TestCaseData(basicOptions, null, "cases/test-case.yaml")
                .SetName("WithCaseFilePath");

            // Test Case 4: Build context with execution ID and case file path
            yield return new TestCaseData(basicOptions, "exec456", "cases/another-case.yaml")
                .SetName("WithExecutionIdAndCaseFilePath");

            // Test Case 5: Build context with overwrite files
            var optionsWithOverwriteFiles = new RunOptions
            {
                ConfigurationFile = "test.yaml",
                OverwriteFiles = new List<string> { "file1.yaml", "file2.yaml" },
                OverwriteArguments = new List<string>(),
                PushReferences = new List<string>()
            };
            yield return new TestCaseData(optionsWithOverwriteFiles, null, null)
                .SetName("WithOverwriteFiles");

            // Test Case 6: Build context with overwrite arguments
            var optionsWithOverwriteArgs = new RunOptions
            {
                ConfigurationFile = "test.yaml",
                OverwriteFiles = new List<string>(),
                OverwriteArguments = new List<string> { "arg1=value1", "arg2=value2" },
                PushReferences = new List<string>()
            };
            yield return new TestCaseData(optionsWithOverwriteArgs, null, null)
                .SetName("WithOverwriteArguments");

            // Test Case 7: Build context with push references
            var optionsWithReferences = new RunOptions
            {
                ConfigurationFile = "test.yaml",
                OverwriteFiles = new List<string>(),
                OverwriteArguments = new List<string>(),
                PushReferences = new List<string> { "Sessions", "ref1.yml", "ref2.yml" }
            };
            yield return new TestCaseData(optionsWithReferences, null, null)
                .SetName("WithPushReferences");

            // Test Case 8: Build context with all options enabled
            var allOptions = new RunOptions
            {
                ConfigurationFile = "full-config.yaml",
                OverwriteFiles = new List<string> { "overwrite1.yaml" },
                OverwriteArguments = new List<string> { "arg=value" },
                PushReferences = new List<string> { "Sessions", "ref1.yml", "ref2.yml" },
                ResolveCasesLast = true,
                DontResolveWithEnvironmentVariables = false
            };
            yield return new TestCaseData(allOptions, "full-execution", "cases/full-case.yaml")
                .SetName("FullOptionsEnabled");
        }

        [Test, TestCaseSource(nameof(TestBuildContextCaseData))]
        public void TestBuildContext_CallFunctionWithCustomRunOptions_ShouldBuildContextCorrectly(
            RunOptions options, string? executionId, string? caseFilePath)
        {
            // Arrange

            // Create mock context builder using Moq
            var mockContextBuilder = new Mock<IContextBuilder>();
            var mockInternalContext = new Mock<InternalContext>();

            // Set up the context builder to return our mock
            var loader = new RunLoader<Runner, RunOptions>(options, executionId);

            // Mock the BuildInternal method to return our mock context
            mockContextBuilder.Setup(cb => cb.BuildInternal()).Returns(mockInternalContext.Object);

            // Act
            var result = (InternalContext?)BuildContextMethodInfo.Invoke(loader,
                [executionId, caseFilePath, mockContextBuilder.Object]);

            // Assert
            Assert.That(result, Is.EqualTo(mockInternalContext.Object));

            // Verify all expected method calls were made
            mockContextBuilder.Verify(cb => cb.SetLogger(It.IsAny<ILogger>()), Times.Once);
            mockContextBuilder.Verify(cb => cb.SetExecutionId(executionId), Times.Once);
            mockContextBuilder.Verify(cb => cb.SetConfigurationFile(options.ConfigurationFile), Times.Once);

            // Verify session setup
            mockContextBuilder.Verify(cb => cb.SetCurrentRunningSessions(It.IsAny<RunningSessions>()), Times.Once);

            // Verify overwrite files
            if (options.OverwriteFiles != null && options.OverwriteFiles.Any())
            {
                foreach (var overwriteFile in options.OverwriteFiles)
                {
                    mockContextBuilder.Verify(cb => cb.WithOverwriteFile(overwriteFile), Times.Once);
                }
            }

            // Verify case setting
            if (caseFilePath != null)
            {
                mockContextBuilder.Verify(cb => cb.SetCase(caseFilePath), Times.Once);
            }
            else
            {
                mockContextBuilder.Verify(cb => cb.SetCase(null), Times.Once);
            }

            // Verify overwrite arguments
            if (options.OverwriteArguments != null && options.OverwriteArguments.Any())
            {
                foreach (var overwriteArg in options.OverwriteArguments)
                {
                    mockContextBuilder.Verify(cb => cb.WithOverwriteArgument(overwriteArg), Times.Once);
                }
            }

            // Verify reference resolutions
            if (options.PushReferences != null && options.PushReferences.Any())
            {
                mockContextBuilder.Verify(cb => cb.WithReferenceResolution(It.IsAny<ReferenceConfig>()),
                    Times.Once);
            }

            // Verify case resolution flags
            if (options.ResolveCasesLast)
            {
                mockContextBuilder.Verify(cb => cb.ResolveCaseLast(), Times.Once);
            }

            if (!options.DontResolveWithEnvironmentVariables)
            {
                mockContextBuilder.Verify(cb => cb.WithEnvironmentVariableResolution(), Times.Once);
            }

            // Verify final build call
            mockContextBuilder.Verify(cb => cb.BuildInternal(), Times.Once);
        }
    }
}