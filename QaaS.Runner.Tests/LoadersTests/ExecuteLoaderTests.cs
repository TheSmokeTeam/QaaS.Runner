using System.Reflection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
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
}
