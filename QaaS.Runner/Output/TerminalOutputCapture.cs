using System.Text;
using Microsoft.Extensions.Logging;

namespace QaaS.Runner.Output;

/// <summary>
/// Tees standard output and standard error to a temporary file while preserving normal terminal output.
/// </summary>
/// <param name="path">The temporary output file path.</param>
/// <param name="writer">The synchronized temporary file writer.</param>
/// <param name="output">The original standard-output writer.</param>
/// <param name="error">The original standard-error writer.</param>
/// <param name="logger">The logger used for passive capture and cleanup failures.</param>
internal sealed class TerminalOutputCapture(
    string path,
    TextWriter writer,
    TextWriter output,
    TextWriter error,
    ILogger logger
) : IDisposable
{
    private bool _stopped;
    private bool _disposed;

    /// <summary>Gets the path of the temporary file containing the captured output.</summary>
    internal string Path => path;

    /// <summary>Starts terminal capture without interrupting execution if initialization fails.</summary>
    /// <param name="logger">The logger used for passive capture and cleanup failures.</param>
    /// <returns>The active capture, or <see langword="null" /> when capture could not be started.</returns>
    internal static TerminalOutputCapture? TryStart(ILogger logger)
    {
        TerminalOutputCapture? capture = null;

        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"qaas-{Guid.NewGuid():N}.log");
            var writer = TextWriter.Synchronized(new StreamWriter(path) { AutoFlush = true });
            var output = Console.Out;
            var error = Console.Error;
            capture = new TerminalOutputCapture(path, writer, output, error, logger);
            Console.SetOut(new TeeTextWriter(output, writer));
            Console.SetError(new TeeTextWriter(error, writer));
            return capture;
        }
        catch (Exception exception)
        {
            capture?.Dispose();
            logger.LogWarning(exception, "Could not capture terminal output.");
            return null;
        }
    }

    /// <summary>Restores the original console writers and closes the temporary file for publishing.</summary>
    internal void Stop()
    {
        if (_stopped) return;
        _stopped = true;

        try
        {
            try { Console.SetOut(output); }
            finally
            {
                try { Console.SetError(error); }
                finally { writer.Dispose(); }
            }
        }
        catch (Exception exception) { logger.LogWarning(exception, "Could not stop terminal output capture cleanly."); }
    }

    /// <summary>Stops capture and deletes the temporary file.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        try { File.Delete(path); }
        catch (Exception exception) { logger.LogWarning(exception, "Could not delete temporary terminal output file."); }
    }

    /// <summary>Mirrors each write to the original terminal and the temporary file.</summary>
    private sealed class TeeTextWriter(TextWriter output, TextWriter file) : TextWriter
    {
        public override Encoding Encoding => output.Encoding;
        public override void Write(char value) { output.Write(value); file.Write(value); }
        public override void Write(string? value) { output.Write(value); file.Write(value); }
        public override void Flush() { output.Flush(); file.Flush(); }
    }
}
