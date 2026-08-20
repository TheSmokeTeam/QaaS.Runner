using NUnit.Framework;
using QaaS.Runner.Output;

namespace QaaS.Runner.Tests.OutputTests;

[TestFixture]
[NonParallelizable]
public class TerminalOutputCaptureTests
{
    [Test]
    public void StopAndDispose_AreIdempotent()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        TerminalOutputCapture? capture = null;
        string? path = null;

        try
        {
            capture = TerminalOutputCapture.TryStart(Globals.Logger);
            Assert.That(capture, Is.Not.Null);
            path = capture!.Path;

            Console.Write("stdout");
            capture.Stop();
            capture.Stop();
            Assert.That(File.ReadAllText(path), Is.EqualTo("stdout"));
            capture.Dispose();
            capture.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(Console.Out, Is.SameAs(originalOut));
                Assert.That(Console.Error, Is.SameAs(originalError));
                Assert.That(File.Exists(path), Is.False);
            });
        }
        finally
        {
            try { capture?.Dispose(); }
            catch { }
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            if (path is not null) File.Delete(path);
        }
    }
}
