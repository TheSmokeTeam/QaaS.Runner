using System.ComponentModel.DataAnnotations;
using CommandLine;
using QaaS.Framework.Configurations.CustomValidationAttributes;
using QaaS.Framework.Executions.Options;

namespace QaaS.Runner.Options;

[Verb("execute", HelpText =
    "Executes a yaml execution file that contains a list of other commands to execute in a sequential order. " +
    "The flags of all commands in the execution file that can also be given in the execute command" +
    " ('s', 'e', 'l', 'g', 'reporter') will be ignored.")]
public record ExecuteOptions : LoggerOptions
{
    [Required]
    [ValidPath]
    [Value(0, Default = Constants.DefaultQaaSExecutionConfigurationFileName,
        HelpText =
            "Path to a yaml configuration file that contains a list of QaaS commands to execute in sequential order.")]
    public string? ConfigurationFile { get; init; }

    [Option("reporter",
        MetaValue = "mode",
        HelpText = "Reporter backend for this invocation. Supported values: allure, reportportal, both. Defaults to both.")]
    public string? Reporter { get; set; }

    [Option('s', "serve-results",
        MetaValue = "folder",
        HelpText = @"
Serves Allure output after executing all commands.
If the flag is provided without a value it serves the default raw results folder 'allure-results'.
Provide a folder name such as 'allure-report' to open a generated report directory, which is useful for Allure 3 flows.
When any of the commands written in the executable configuration file use this flag it will not do anything, this is the deciding flag.
Uses a locally installed allure CLI tool, if allure CLI is not installed and added to path the serve will fail.
")]
    public string? ServeResultsFolder { get; set; }

    public bool AutoServeTestResults
    {
        get => !string.IsNullOrWhiteSpace(ServeResultsFolder);
        set => ServeResultsFolder = value
            ? string.IsNullOrWhiteSpace(ServeResultsFolder)
                ? AssertableOptions.DefaultServeResultsFolder
                : ServeResultsFolder.Trim()
            : null;
    }

    public string GetServeResultsFolderOrDefault()
    {
        return string.IsNullOrWhiteSpace(ServeResultsFolder)
            ? AssertableOptions.DefaultServeResultsFolder
            : ServeResultsFolder.Trim();
    }

    [Option('e', "empty-allure-directory", Default = false,
        HelpText = "If flag is enabled will automatically empty the allure results directory before running.")]
    public bool EmptyAllureDirectory { get; set; } = false;

    [Option('c', "command-ids-to-run", Default = null,
        HelpText =
            "Ids of the commands to run. Only the commands given would run. If none is given runs all commands.")]
    public IList<string> CommandIdsToRun { get; init; } = Array.Empty<string>();

    [Option("no-process-exit", Default = false,
        HelpText =
            "When this flag is used the runner will not terminate the current process after it completes. " +
            "Useful when embedding QaaS.Runner and orchestrating multiple runners in a single host process.")]
    public bool NoProcessExit { get; init; } = false;

    public ReporterMode GetReporterModeOrDefault()
    {
        if (string.IsNullOrWhiteSpace(Reporter))
            return ReporterMode.Both;

        if (Enum.TryParse<ReporterMode>(Reporter.Trim(), ignoreCase: true, out var reporterMode))
            return reporterMode;

        throw new ArgumentException(
            $"Unsupported reporter mode '{Reporter}'. Supported values: allure, reportportal, both.",
            nameof(Reporter));
    }
}
