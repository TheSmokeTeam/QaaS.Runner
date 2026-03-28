using System.Reflection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using QaaS.Framework.Configurations.CustomExceptions;
using QaaS.Runner.ConfigurationObjects;
using QaaS.Runner.Loaders;
using QaaS.Runner.Options;

namespace QaaS.Runner.Tests.LoadersTests;

public class ExecuteLoaderTests
{
    private static readonly MethodInfo? GetCommandsToRunMethodInfo = typeof(ExecuteLoader<Runner>).GetMethod(
        "GetCommandsToRun", BindingFlags.NonPublic | BindingFlags.Instance)!;


    private static IEnumerable<TestCaseData> TestGetCommandsToRunCaseData()
    {
        var allCommandList = new List<CommandConfig>
        {
            new()
            {
                Id = "1",
                Command = "1"
            },
            new()
            {
                Id = "2",
                Command = "2"
            },
            new()
            {
                Id = "3",
                Command = "3"
            }
        };
        yield return new TestCaseData(new ExecuteOptions { ConfigurationFile = "executable.yaml", SendLogs = false }, allCommandList,
            allCommandList).SetName("TestGetCommandsToRunAllCommands");
        yield return new TestCaseData(new ExecuteOptions
            {
                ConfigurationFile = "executable.yaml",
                CommandIdsToRun = new List<string> { "1" },
                SendLogs = false
            }, allCommandList, new List<CommandConfig> { new() { Command = "1", Id = "1" } })
            .SetName("TestGetCommandsToRunSpecificCommands");
        yield return new TestCaseData(new ExecuteOptions
            {
                ConfigurationFile = "executable.yaml",
                CommandIdsToRun = new List<string> { "7" },
                SendLogs = false
            }, allCommandList, null)
            .SetName("TestGetCommandsToNotFindAnyCommand");
    }

    [Test]
    [TestCaseSource(nameof(TestGetCommandsToRunCaseData))]
    public void TestGetCommandsToRun_CallFunctionWithCustomExecuteOptions_ShouldReturnExpectedOutput(
        ExecuteOptions executeOptions, List<CommandConfig> input, List<CommandConfig>? expectedOutput)
    {
        // Arrange
        var executor = new ExecuteLoader<Runner>(executeOptions);

        // Act
        IEnumerable<CommandConfig>? commandsRan = null;
        try
        {
            commandsRan =
                (IEnumerable<CommandConfig>)GetCommandsToRunMethodInfo!.Invoke(executor,
                    [input])!;
        }
        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException)
        {
            Globals.Logger.LogDebug("Expectedly crashed cause no command ids");
        }

        // Assert
        Assert.That(commandsRan, Is.EqualTo(expectedOutput));
    }

    [Test]
    public void GetLoadedRunner_WhenExecutableCommandContainsExecute_ThrowsArgumentException()
    {
        var executablePath = Path.Combine(Path.GetTempPath(), $"qaas-executable-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(executablePath,
            """
            Commands:
              - Id: Loop
                Command: execute run TestData/test.qaas.yaml
            """);

        try
        {
            var loader = new ExecuteLoader<Runner>(new ExecuteOptions
            {
                ConfigurationFile = executablePath,
                SendLogs = false
            });

            var ex = Assert.Throws<ArgumentException>(() => loader.GetLoadedRunner());
            Assert.That(ex!.Message, Does.Contain("Execute configurations cannot contain nested execute commands."));
            Assert.That(ex.Message, Does.Contain("Command id: Loop"));
        }
        finally
        {
            if (File.Exists(executablePath))
            {
                File.Delete(executablePath);
            }
        }
    }

    [Test]
    public void GetLoadedRunner_WhenExecuteConfigurationYamlIsMalformed_ThrowsIndicativeInvalidConfigurationsException()
    {
        var executablePath = Path.Combine(Path.GetTempPath(), $"qaas-executable-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(executablePath,
            """
            Commands:
              - Id: broken
                Command: [broken
            """);

        try
        {
            var loader = new ExecuteLoader<Runner>(new ExecuteOptions
            {
                ConfigurationFile = executablePath,
                SendLogs = false
            });

            var ex = Assert.Throws<InvalidConfigurationsException>(() => loader.GetLoadedRunner());

            Assert.That(ex!.Message, Does.Contain("YAML configuration file is invalid and QaaS cannot continue."));
            Assert.That(ex.Message, Does.Contain($"Resolved local path: {executablePath}"));
            Assert.That(ex.Message, Does.Contain("Parser detail: While parsing a flow sequence"));
        }
        finally
        {
            if (File.Exists(executablePath))
            {
                File.Delete(executablePath);
            }
        }
    }

    [Test]
    public void GetLoadedRunner_WithNoProcessExitFlag_DisablesProcessExitOnCompletion()
    {
        var loader = new ExecuteLoader<Runner>(new ExecuteOptions
        {
            ConfigurationFile = "TestData/executable.yaml",
            SendLogs = false,
            NoProcessExit = true
        });

        var runner = loader.GetLoadedRunner();

        Assert.That(runner.ExitProcessOnCompletion, Is.False);
    }

    [Test]
    public void GetLoadedRunner_WithCustomServeResultsFolder_PassesFolderToRunner()
    {
        var loader = new ExecuteLoader<Runner>(new ExecuteOptions
        {
            ConfigurationFile = "TestData/executable.yaml",
            SendLogs = false,
            ServeResultsFolder = "allure-report"
        });

        var runner = loader.GetLoadedRunner();
        var serveResultsProperty = typeof(Runner).GetProperty("ServeResults", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var serveResultsFolderProperty = typeof(Runner).GetProperty("ServeResultsFolder", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Multiple(() =>
        {
            Assert.That((bool)serveResultsProperty.GetValue(runner)!, Is.True);
            Assert.That((string)serveResultsFolderProperty.GetValue(runner)!, Is.EqualTo("allure-report"));
        });
    }

    [Test]
    public void GetCommandsToRun_WithMissingCommandIds_IncludesAvailableIdsInExceptionMessage()
    {
        var loader = new ExecuteLoader<Runner>(new ExecuteOptions
        {
            ConfigurationFile = "execute.yaml",
            CommandIdsToRun = ["missing"],
            SendLogs = false
        });

        var commandList = new List<CommandConfig>
        {
            new() { Id = "present-a", Command = "run a.qaas.yaml" },
            new() { Id = "present-b", Command = "run b.qaas.yaml" }
        };

        var ex = Assert.Throws<TargetInvocationException>(() => GetCommandsToRunMethodInfo!.Invoke(loader, [commandList]));

        Assert.That(ex!.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(ex.InnerException!.Message,
            Does.Contain("The command-ids-to-run filter contains command ids that do not exist in the execute configuration."));
        Assert.That(ex.InnerException.Message, Does.Contain("Requested command ids not found: missing"));
        Assert.That(ex.InnerException.Message, Does.Contain("Available command ids: present-a, present-b"));
    }

    [Test]
    public void GetLoadedRunner_WhenExecuteConfigurationFileIsMissing_ThrowsCouldNotFindConfigurationException()
    {
        var missingPath = $"missing-execute-{Guid.NewGuid():N}.yaml";
        var loader = new ExecuteLoader<Runner>(new ExecuteOptions
        {
            ConfigurationFile = missingPath,
            SendLogs = false
        });

        var ex = Assert.Throws<CouldNotFindConfigurationException>(() => loader.GetLoadedRunner());

        Assert.That(ex!.Message, Does.Contain("Execute configuration file was not found."));
        Assert.That(ex.Message, Does.Contain($"Configured path: {missingPath}"));
    }

    [Test]
    public void GetLoadedRunner_WhenExecuteConfigurationPathIsUnreadable_PreservesAccessFailure()
    {
        var configurationDirectoryPath = Path.Combine(Path.GetTempPath(), $"qaas-execute-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configurationDirectoryPath);

        try
        {
            var loader = new ExecuteLoader<Runner>(new ExecuteOptions
            {
                ConfigurationFile = configurationDirectoryPath,
                SendLogs = false
            });

            Assert.Throws<UnauthorizedAccessException>(() => loader.GetLoadedRunner());
        }
        finally
        {
            Directory.Delete(configurationDirectoryPath);
        }
    }
}
