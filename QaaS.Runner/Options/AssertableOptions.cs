using CommandLine;

namespace QaaS.Runner.Options;

/// <summary>
///     Base options for any runnable command that also performs an assertion
/// </summary>
public abstract record
    AssertableOptions : BaseOptions
{
    [Option('s', "serve-results", Default = false,
        HelpText = @"
If flag is enabled will automatically serve the test results in a human readable manner after performing the assertions.")]
    public bool AutoServeTestResults { get; set; } = false;

    [Option('e', "empty-results-directory", Default = false,
        HelpText = "If flag is enabled will automatically empty the results directory before running.")]
    public bool EmptyAllureDirectory { get; set; } = false;
}