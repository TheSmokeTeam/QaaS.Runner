using CommandLine;

namespace QaaS.Runner.Options;

/// <summary>
///     Base options for any runnable command that also performs an assertion
/// </summary>
public abstract record
    AssertableOptions : BaseOptions
{
    public const string DefaultServeResultsFolder = "allure-results";

    [Option('s', "serve-results",
        MetaValue = "folder",
        HelpText = @"
Serves Allure output after performing the assertions.
If the flag is provided without a value it serves the default raw results folder 'allure-results'.
Provide a folder name such as 'allure-report' to open a generated report directory, which is useful for Allure 3 flows.")]
    public string? ServeResultsFolder { get; set; }

    public bool AutoServeTestResults
    {
        get => !string.IsNullOrWhiteSpace(ServeResultsFolder);
        set => ServeResultsFolder = value
            ? string.IsNullOrWhiteSpace(ServeResultsFolder)
                ? DefaultServeResultsFolder
                : ServeResultsFolder.Trim()
            : null;
    }

    public string GetServeResultsFolderOrDefault()
    {
        return string.IsNullOrWhiteSpace(ServeResultsFolder)
            ? DefaultServeResultsFolder
            : ServeResultsFolder.Trim();
    }

    [Option('e', "empty-results-directory", Default = false,
        HelpText = "If flag is enabled will automatically empty the results directory before running.")]
    public bool EmptyAllureDirectory { get; set; } = false;
}
