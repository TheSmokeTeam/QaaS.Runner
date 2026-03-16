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
        using var allureServeProcess = Process.Start(new ProcessStartInfo
        {
            WorkingDirectory = Directory.GetCurrentDirectory(),
            FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd" :
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "bash" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "bash" :
                throw new InvalidOperationException("Unknown OS"),
            Arguments = $"/c {allureRunnablePath} serve",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Failed to start Allure serve process.");

        // Log process output with logger
        while (!allureServeProcess.StandardOutput.EndOfStream)
            Console.Out.WriteLine(allureServeProcess.StandardOutput.ReadLine());
        while (!allureServeProcess.StandardError.EndOfStream)
            Console.Out.WriteLine(allureServeProcess.StandardError.ReadLine());
    }
}
