using System.Diagnostics;
using System.Runtime.InteropServices;
using Allure.Commons;

namespace QaaS.Runner.WrappedExternals;

/// <summary>
///     Wraps the external allure CLI functionality for easy usage through code
/// </summary>
public class AllureWrapper
{
    /// <summary>
    ///     The default allure runnable path
    /// </summary>
    public const string DefaultAllureRunnablePath = "allure";

    private const string HistoryDirectoryName = "history";

    /// <summary>
    ///     Constructor
    /// </summary>
    public AllureWrapper()
    {
    }

    /// <summary>
    ///     Cleans the allure results directory
    /// </summary>
    public virtual void CleanTestResultsDirectory()
    {
        var resultsDirectory = ResolveResultsDirectory();
        if (!Directory.Exists(resultsDirectory))
        {
            Directory.CreateDirectory(resultsDirectory);
            Console.Out.WriteLine($"Cleaned allure results directory {resultsDirectory}");
            return;
        }

        foreach (var file in Directory.EnumerateFiles(resultsDirectory))
            File.Delete(file);

        foreach (var directory in Directory.EnumerateDirectories(resultsDirectory))
        {
            if (string.Equals(Path.GetFileName(directory), HistoryDirectoryName, StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.Delete(directory, true);
        }

        Console.Out.WriteLine($"Cleaned allure results directory {resultsDirectory}");
    }

    /// <summary>
    ///     Automatically serves the test results in a human-readable manner
    /// </summary>
    public virtual void ServeTestResults(string allureRunnablePath = DefaultAllureRunnablePath)
    {
        var resolvedAllureRunnablePath = ResolveAllureRunnablePath(allureRunnablePath);
        var temporaryReportDirectory = CreateTemporaryReportDirectory();

        try
        {
            RunProcess(CreateGenerateProcessStartInfo(resolvedAllureRunnablePath, temporaryReportDirectory));
            CopyGeneratedHistoryToResultsDirectory(temporaryReportDirectory);
        }
        finally
        {
            if (Directory.Exists(temporaryReportDirectory))
                Directory.Delete(temporaryReportDirectory, true);
        }

        RunProcess(CreateServeProcessStartInfo(resolvedAllureRunnablePath));
    }

    private void RunProcess(ProcessStartInfo startInfo)
    {
        using var process = StartProcess(startInfo);

        while (!process.StandardOutput.EndOfStream)
            Console.Out.WriteLine(process.StandardOutput.ReadLine());
        while (!process.StandardError.EndOfStream)
            Console.Out.WriteLine(process.StandardError.ReadLine());
    }

    /// <summary>
    ///     Starts the configured shell process for serving Allure results. Tests override this seam
    ///     to replace the long-lived external process with a deterministic short-lived one.
    /// </summary>
    protected virtual Process StartProcess(ProcessStartInfo startInfo)
    {
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Allure serve process.");
    }

    /// <summary>
    ///     Builds the shell command used to refresh the report history in a temporary report directory.
    /// </summary>
    protected virtual ProcessStartInfo CreateGenerateProcessStartInfo(string allureRunnablePath,
        string generatedReportDirectory)
    {
        return CreateAllureProcessStartInfo(allureRunnablePath,
            $"generate {QuoteForShell(ResolveResultsDirectory())} -o {QuoteForShell(generatedReportDirectory)} --clean");
    }

    /// <summary>
    ///     Builds the shell command used to launch <c>allure serve</c> directly from the results directory.
    /// </summary>
    protected virtual ProcessStartInfo CreateServeProcessStartInfo(string allureRunnablePath)
    {
        return CreateAllureProcessStartInfo(allureRunnablePath, $"serve {QuoteForShell(ResolveResultsDirectory())}");
    }

    /// <summary>
    ///     Builds the shell command used to launch an Allure CLI subcommand. Empty or whitespace inputs
    ///     intentionally fall back to the default executable name so callers do not have to special-case it.
    /// </summary>
    protected virtual ProcessStartInfo CreateAllureProcessStartInfo(string allureRunnablePath, string subCommand)
    {
        var resolvedAllureRunnablePath = ResolveAllureRunnablePath(allureRunnablePath);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new ProcessStartInfo
            {
                WorkingDirectory = ResolveWorkingDirectory(),
                FileName = "cmd",
                Arguments = $"/c {resolvedAllureRunnablePath} {subCommand}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new ProcessStartInfo
            {
                WorkingDirectory = ResolveWorkingDirectory(),
                FileName = "bash",
                Arguments = $"-lc \"{resolvedAllureRunnablePath} {subCommand}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
        }

        throw new InvalidOperationException("Unknown OS");
    }

    protected virtual string ResolveWorkingDirectory()
    {
        return Directory.GetCurrentDirectory();
    }

    protected virtual string ResolveResultsDirectory()
    {
        return Path.GetFullPath(AllureLifecycle.Instance.ResultsDirectory, ResolveWorkingDirectory());
    }

    protected virtual string CreateTemporaryReportDirectory()
    {
        return Path.Combine(Path.GetTempPath(), $"qaas-allure-{Guid.NewGuid():N}");
    }

    protected virtual void CopyGeneratedHistoryToResultsDirectory(string generatedReportDirectory)
    {
        var generatedHistoryDirectory = Path.Combine(generatedReportDirectory, HistoryDirectoryName);
        if (!Directory.Exists(generatedHistoryDirectory))
            return;

        var resultsHistoryDirectory = Path.Combine(ResolveResultsDirectory(), HistoryDirectoryName);
        if (Directory.Exists(resultsHistoryDirectory))
            Directory.Delete(resultsHistoryDirectory, true);

        CopyDirectory(generatedHistoryDirectory, resultsHistoryDirectory);
        Console.Out.WriteLine($"Restored allure history to {resultsHistoryDirectory}");
    }

    private static string ResolveAllureRunnablePath(string allureRunnablePath)
    {
        return string.IsNullOrWhiteSpace(allureRunnablePath)
            ? DefaultAllureRunnablePath
            : allureRunnablePath;
    }

    private static string QuoteForShell(string path)
    {
        return $"\"{path}\"";
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory))
        {
            var destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
            File.Copy(sourceFile, destinationFile, overwrite: true);
        }

        foreach (var sourceSubDirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var destinationSubDirectory = Path.Combine(destinationDirectory, Path.GetFileName(sourceSubDirectory));
            CopyDirectory(sourceSubDirectory, destinationSubDirectory);
        }
    }
}
