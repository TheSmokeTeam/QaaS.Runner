using System.ComponentModel.DataAnnotations;
using CommandLine;
using QaaS.Framework.Configurations.CustomValidationAttributes;
using QaaS.Framework.Executions.Options;

namespace QaaS.Runner.Options;

[Verb("execute", HelpText =
    "Executes a yaml execution file that contains a list of other commands to execute in a sequential order. " +
    "The flags of all commands in the execution file that can also be given in the execute command" +
    " ('s', 'e', 'l', 'g') will be ignored.")]
public record ExecuteOptions : LoggerOptions
{
    [Required]
    [ValidPath]
    [Value(0, Default = Constants.DefaultQaaSExecutionConfigurationFileName,
        HelpText =
            "Path to a yaml configuration file that contains a list of QaaS commands to execute in sequential order.")]
    public string? ConfigurationFile { get; init; }

    [Option('s', "serve-results", Default = false,
        HelpText = @"
If flag is enabled will automatically serve the test results in a human readable manner using allure after executing all commands.
when any of the commands written in the executable configuration file use this flag it will not do anything, this is the deciding flag.
Uses a locally installed allure CLI tool, if allure CLI is not installed and added to path the serve will fail.
")]
    public bool AutoServeTestResults { get; set; } = false;

    [Option('e', "empty-allure-directory", Default = false,
        HelpText = "If flag is enabled will automatically empty the allure results directory before running.")]
    public bool EmptyAllureDirectory { get; set; } = false;

    [Option('c', "command-ids-to-run", Default = null,
        HelpText =
            "Ids of the commands to run. Only the commands given would run. If none is given runs all commands.")]
    public IList<string> CommandIdsToRun { get; init; } = Array.Empty<string>();
}