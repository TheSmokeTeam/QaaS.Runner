using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Moq;
using QaaS.Framework.Configurations;
using QaaS.Framework.Configurations.References;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Loaders;
using QaaS.Runner.Options;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Runner.Artifactory;

namespace QaaS.Runner.Tests.LoadersTests
{
    [TestFixture]
    public class RunLoaderTests
    {
        private static readonly MethodInfo BuildContextMethodInfo = typeof(RunLoader<Runner, RunOptions>).GetMethod(
            "BuildContext", BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static readonly MethodInfo GetLoadedContextsMethodInfo = typeof(RunLoader<Runner, RunOptions>).GetMethod(
            "GetLoadedContexts", BindingFlags.NonPublic | BindingFlags.Instance)!;

        private sealed class TestRunLoader : RunLoader<Runner, RunOptions>
        {
            public List<(string? ExecutionId, string? RelativeCasePath)> BuildContextCalls { get; } = [];

            public TestRunLoader(RunOptions options, string? executionId = null) : base(options, executionId)
            {
            }

            protected override InternalContext BuildContext(string? executionId, string? relativeCaseFilePath = null,
                IContextBuilder? contextBuilder = null)
            {
                BuildContextCalls.Add((executionId, relativeCaseFilePath));
                return new InternalContext
                {
                    Logger = Globals.Logger,
                    ExecutionId = executionId,
                    CaseName = relativeCaseFilePath,
                    RootConfiguration = new ConfigurationBuilder().Build(),
                    InternalRunningSessions = new RunningSessions(
                        new Dictionary<string, RunningSessionData<object, object>>())
                };
            }
        }

        private sealed class TestJfrogArtifactoryHelper(IEnumerable<string> files) : IJfrogArtifactoryHelper
        {
            public Task<IReadOnlyList<string>> GetUrlsToAllFilesInArtifactoryFolderAsync(string artifactoryFolderUrl,
                HttpClient httpClient, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<string>>(files.ToList());
            }

            public IEnumerable<string> GetUrlsToAllFilesInArtifactoryFolder(string artifactoryFolderUrl,
                HttpClient httpClient)
            {
                return files;
            }
        }

        private static IEnumerable<TestCaseData> TestBuildContextCaseData()
        {
            // Test Case 1: Basic build context with minimal options
            var basicOptions = new RunOptions
            {
                ConfigurationFile = "test.yaml",
                OverwriteFiles = new List<string>(),
                OverwriteArguments = new List<string>(),
                PushReferences = new List<string>(),
                SendLogs = false
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
                PushReferences = new List<string>(),
                SendLogs = false
            };
            yield return new TestCaseData(optionsWithOverwriteFiles, null, null)
                .SetName("WithOverwriteFiles");

            // Test Case 6: Build context with overwrite arguments
            var optionsWithOverwriteArgs = new RunOptions
            {
                ConfigurationFile = "test.yaml",
                OverwriteFiles = new List<string>(),
                OverwriteArguments = new List<string> { "arg1=value1", "arg2=value2" },
                PushReferences = new List<string>(),
                SendLogs = false
            };
            yield return new TestCaseData(optionsWithOverwriteArgs, null, null)
                .SetName("WithOverwriteArguments");

            // Test Case 7: Build context with push references
            var optionsWithReferences = new RunOptions
            {
                ConfigurationFile = "test.yaml",
                OverwriteFiles = new List<string>(),
                OverwriteArguments = new List<string>(),
                PushReferences = new List<string> { "Sessions", "ref1.yml", "ref2.yml" },
                SendLogs = false
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
                DontResolveWithEnvironmentVariables = false,
                SendLogs = false
            };
            yield return new TestCaseData(allOptions, "full-execution", "cases/full-case.yaml")
                .SetName("FullOptionsEnabled");

            // Test Case 9: Build context without environment variable resolution
            var noEnvResolutionOptions = new RunOptions
            {
                ConfigurationFile = "no-env.yaml",
                OverwriteFiles = new List<string>(),
                OverwriteArguments = new List<string>(),
                PushReferences = new List<string>(),
                DontResolveWithEnvironmentVariables = true,
                SendLogs = false
            };
            yield return new TestCaseData(noEnvResolutionOptions, "no-env-execution", null)
                .SetName("WithNoEnvironmentVariableResolution");
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
            else
            {
                mockContextBuilder.Verify(cb => cb.WithEnvironmentVariableResolution(), Times.Never);
            }

            // Verify final build call
            mockContextBuilder.Verify(cb => cb.BuildInternal(), Times.Once);
        }

        [Test]
        public void GetLoadedContexts_WithoutCasesRootDirectory_BuildsSingleContext()
        {
            var options = new RunOptions
            {
                ConfigurationFile = "test.yaml",
                SendLogs = false,
                CasesRootDirectory = null
            };

            var loader = new TestRunLoader(options, "exec-1");

            var contexts = ((IEnumerable<InternalContext>)GetLoadedContextsMethodInfo.Invoke(loader, null)!).ToList();

            Assert.That(contexts, Has.Count.EqualTo(1));
            Assert.That(loader.BuildContextCalls, Has.Count.EqualTo(1));
            Assert.That(loader.BuildContextCalls[0].ExecutionId, Is.EqualTo("exec-1"));
            Assert.That(loader.BuildContextCalls[0].RelativeCasePath, Is.Null);
        }

        [Test]
        public void GetLoadedContexts_WithFileCasesAndCaseNameFilter_ReturnsOnlyRequestedCases()
        {
            var relativeCasesDir = $"TestData\\RunLoaderCases-{Guid.NewGuid():N}";
            var absoluteCasesDir = Path.Combine(Environment.CurrentDirectory, relativeCasesDir);
            Directory.CreateDirectory(Path.Combine(absoluteCasesDir, "nested"));

            var caseA = Path.Combine(absoluteCasesDir, "a.yaml");
            var caseB = Path.Combine(absoluteCasesDir, "nested", "b.yaml");
            File.WriteAllText(caseA, "A");
            File.WriteAllText(caseB, "B");

            try
            {
                var options = new RunOptions
                {
                    ConfigurationFile = "test.yaml",
                    SendLogs = false,
                    CasesRootDirectory = relativeCasesDir,
                    CasesNamesToRun = [Path.GetRelativePath(Environment.CurrentDirectory, caseA)]
                };

                var loader = new TestRunLoader(options, "exec-2");
                var contexts =
                    ((IEnumerable<InternalContext>)GetLoadedContextsMethodInfo.Invoke(loader, null)!).ToList();

                Assert.That(contexts, Has.Count.EqualTo(1));
                Assert.That(contexts[0].CaseName,
                    Is.EqualTo(Path.GetRelativePath(Environment.CurrentDirectory, caseA)));
            }
            finally
            {
                Directory.Delete(absoluteCasesDir, true);
            }
        }

        [Test]
        public void GetLoadedContexts_WithUnknownCaseName_ThrowsInvalidOperationException()
        {
            var relativeCasesDir = $"TestData\\RunLoaderMissingCase-{Guid.NewGuid():N}";
            var absoluteCasesDir = Path.Combine(Environment.CurrentDirectory, relativeCasesDir);
            Directory.CreateDirectory(absoluteCasesDir);
            File.WriteAllText(Path.Combine(absoluteCasesDir, "only-case.yaml"), "content");

            try
            {
                var options = new RunOptions
                {
                    ConfigurationFile = "test.yaml",
                    SendLogs = false,
                    CasesRootDirectory = relativeCasesDir,
                    CasesNamesToRun = ["missing-case.yaml"]
                };

                var loader = new TestRunLoader(options, "exec-3");

                var ex = Assert.Throws<TargetInvocationException>(() => GetLoadedContextsMethodInfo.Invoke(loader, null));
                Assert.That(ex!.InnerException, Is.TypeOf<InvalidOperationException>());
                Assert.That(ex.InnerException!.Message, Does.Contain("Found none existing cases names"));
            }
            finally
            {
                Directory.Delete(absoluteCasesDir, true);
            }
        }

        [Test]
        public void GetLoadedContexts_WithIgnoredCaseNames_ExcludesIgnoredCases()
        {
            var relativeCasesDir = $"TestData\\RunLoaderIgnoreExact-{Guid.NewGuid():N}";
            var absoluteCasesDir = Path.Combine(Environment.CurrentDirectory, relativeCasesDir);
            Directory.CreateDirectory(absoluteCasesDir);

            var keepCase = Path.Combine(absoluteCasesDir, "keep.yaml");
            var ignoreCase = Path.Combine(absoluteCasesDir, "ignore.yaml");
            File.WriteAllText(keepCase, "keep");
            File.WriteAllText(ignoreCase, "ignore");

            try
            {
                var options = new RunOptions
                {
                    ConfigurationFile = "test.yaml",
                    SendLogs = false,
                    CasesRootDirectory = relativeCasesDir,
                    CasesNamesToIgnore = [Path.GetRelativePath(Environment.CurrentDirectory, ignoreCase)]
                };

                var loader = new TestRunLoader(options, "exec-ignore-exact");
                var contexts =
                    ((IEnumerable<InternalContext>)GetLoadedContextsMethodInfo.Invoke(loader, null)!).ToList();

                Assert.That(contexts.Select(context => context.CaseName).ToArray(), Is.EqualTo(new[]
                {
                    Path.GetRelativePath(Environment.CurrentDirectory, keepCase)
                }));
            }
            finally
            {
                Directory.Delete(absoluteCasesDir, true);
            }
        }

        [Test]
        public void GetLoadedContexts_WithIgnoredCasePatterns_ExcludesPatternMatches()
        {
            var relativeCasesDir = $"TestData\\RunLoaderIgnorePattern-{Guid.NewGuid():N}";
            var absoluteCasesDir = Path.Combine(Environment.CurrentDirectory, relativeCasesDir);
            Directory.CreateDirectory(Path.Combine(absoluteCasesDir, "nested"));

            var keepCase = Path.Combine(absoluteCasesDir, "alpha.yaml");
            var ignoreCase = Path.Combine(absoluteCasesDir, "nested", "skip-beta.yaml");
            File.WriteAllText(keepCase, "keep");
            File.WriteAllText(ignoreCase, "ignore");

            try
            {
                var options = new RunOptions
                {
                    ConfigurationFile = "test.yaml",
                    SendLogs = false,
                    CasesRootDirectory = relativeCasesDir,
                    CasesNamePatternsToIgnore = [@"skip-.*\.yaml$"]
                };

                var loader = new TestRunLoader(options, "exec-ignore-pattern");
                var contexts =
                    ((IEnumerable<InternalContext>)GetLoadedContextsMethodInfo.Invoke(loader, null)!).ToList();

                Assert.That(contexts.Select(context => context.CaseName).ToArray(), Is.EqualTo(new[]
                {
                    Path.GetRelativePath(Environment.CurrentDirectory, keepCase)
                }));
            }
            finally
            {
                Directory.Delete(absoluteCasesDir, true);
            }
        }

        [Test]
        public void GetLoadedContexts_WithHttpCasesRootDirectory_UsesJfrogHelperResults()
        {
            var options = new RunOptions
            {
                ConfigurationFile = "test.yaml",
                SendLogs = false,
                CasesRootDirectory = "https://artifactory.example.com/cases"
            };

            var loader = new TestRunLoader(options, "exec-5");
            var helperField = typeof(RunLoader<Runner, RunOptions>)
                .GetField("_jfrogArtifactoryHelper", BindingFlags.Instance | BindingFlags.NonPublic)!;
            helperField.SetValue(loader, new TestJfrogArtifactoryHelper(
                [
                    "https://artifactory.example.com/cases/case-b.yaml",
                    "https://artifactory.example.com/cases/case-a.yaml"
                ]));

            var contexts = ((IEnumerable<InternalContext>)GetLoadedContextsMethodInfo.Invoke(loader, null)!).ToList();

            Assert.That(contexts, Has.Count.EqualTo(2));
            Assert.That(loader.BuildContextCalls.Select(call => call.RelativeCasePath).ToArray(), Is.EqualTo(new[]
            {
                "https://artifactory.example.com/cases/case-a.yaml",
                "https://artifactory.example.com/cases/case-b.yaml"
            }));
        }

        [Test]
        public void GetLoadedRunner_WithAssertableOptions_SetsRunnerFlagsAndBuilders()
        {
            var options = new RunOptions
            {
                ConfigurationFile = "test.yaml",
                SendLogs = false,
                EmptyAllureDirectory = true,
                AutoServeTestResults = true
            };

            var loader = new TestRunLoader(options, "exec-4");

            var runner = loader.GetLoadedRunner();

            Assert.That(runner, Is.Not.Null);
            Assert.That(runner.ExecutionBuilders, Has.Count.EqualTo(1));

            var emptyResultsProperty = typeof(Runner).GetProperty("EmptyResults", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var serveResultsProperty = typeof(Runner).GetProperty("ServeResults", BindingFlags.Instance | BindingFlags.NonPublic)!;

            Assert.That((bool)emptyResultsProperty.GetValue(runner)!, Is.True);
            Assert.That((bool)serveResultsProperty.GetValue(runner)!, Is.True);
        }
    }
}
