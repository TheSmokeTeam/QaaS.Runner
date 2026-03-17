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
        AllureLifecycle.Instance.CleanupResultDirectory();
        Console.Out.WriteLine($"Cleaned allure results directory {AllureLifecycle.Instance.ResultsDirectory}");
    }

    /// <summary>
    ///     Automatically serves the test results in a human-readable manner
    /// </summary>
    public virtual void ServeTestResults(string allureRunnablePath = DefaultAllureRunnablePath)
    {
        using var allureServeProcess = StartProcess(CreateServeProcessStartInfo(allureRunnablePath));

        // Log process output with logger
        while (!allureServeProcess.StandardOutput.EndOfStream)
            Console.Out.WriteLine(allureServeProcess.StandardOutput.ReadLine());
        while (!allureServeProcess.StandardError.EndOfStream)
            Console.Out.WriteLine(allureServeProcess.StandardError.ReadLine());
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
    ///     Builds the shell command used to launch <c>allure serve</c>. Empty or whitespace inputs
    ///     intentionally fall back to the default executable name so callers do not have to special-case it.
    /// </summary>
    protected virtual ProcessStartInfo CreateServeProcessStartInfo(string allureRunnablePath)
    {
        var resolvedAllureRunnablePath = string.IsNullOrWhiteSpace(allureRunnablePath)
            ? DefaultAllureRunnablePath
            : allureRunnablePath;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new ProcessStartInfo
            {
                WorkingDirectory = Directory.GetCurrentDirectory(),
                FileName = "cmd",
                Arguments = $"/c {resolvedAllureRunnablePath} serve",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new ProcessStartInfo
            {
                WorkingDirectory = Directory.GetCurrentDirectory(),
                FileName = "bash",
                Arguments = $"-lc \"{resolvedAllureRunnablePath} serve\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
        }

        throw new InvalidOperationException("Unknown OS");
    }
}
