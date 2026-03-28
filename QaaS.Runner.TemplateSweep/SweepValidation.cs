using RunnerBootstrap = QaaS.Runner.Bootstrap;
using MockerBootstrap = QaaS.Mocker.Bootstrap;

internal static class SweepValidation
{
    public static ValidationOutcome Validate(ValidationKind validationKind, string configPath)
    {
        var previousDirectory = Environment.CurrentDirectory;
        var previousOut = Console.Out;
        var previousError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        Console.SetOut(stdout);
        Console.SetError(stderr);
        Environment.CurrentDirectory = Path.GetDirectoryName(configPath)!;
        Environment.ExitCode = 0;

        try
        {
            return validationKind switch
            {
                ValidationKind.RunnerTemplate => ValidateRunnerTemplate(configPath, stdout, stderr),
                ValidationKind.RunnerExecute => ValidateRunnerExecute(configPath, stdout, stderr),
                ValidationKind.MockerTemplate => ValidateMockerTemplate(configPath, stdout, stderr),
                _ => throw new ArgumentOutOfRangeException(nameof(validationKind), validationKind, "Unsupported validation kind.")
            };
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
            Environment.CurrentDirectory = previousDirectory;
        }
    }

    private static ValidationOutcome ValidateRunnerTemplate(string configPath, StringWriter stdout, StringWriter stderr)
    {
        try
        {
            var runner = RunnerBootstrap.New(["template", configPath, "--no-process-exit", "--no-env"]);
            runner.ExitProcessOnCompletion = false;
            var exitCode = runner.RunAndGetExitCode();
            return new ValidationOutcome(
                exitCode == 0,
                exitCode,
                BuildSummary(stdout, stderr),
                stdout.ToString() + stderr.ToString());
        }
        catch (Exception exception)
        {
            return Failure(exception, stdout, stderr);
        }
    }

    private static ValidationOutcome ValidateRunnerExecute(string configPath, StringWriter stdout, StringWriter stderr)
    {
        try
        {
            var runner = RunnerBootstrap.New(["execute", configPath, "--no-process-exit"]);
            runner.ExitProcessOnCompletion = false;
            var exitCode = runner.RunAndGetExitCode();
            return new ValidationOutcome(
                exitCode == 0,
                exitCode,
                BuildSummary(stdout, stderr),
                stdout.ToString() + stderr.ToString());
        }
        catch (Exception exception)
        {
            return Failure(exception, stdout, stderr);
        }
    }

    private static ValidationOutcome ValidateMockerTemplate(string configPath, StringWriter stdout, StringWriter stderr)
    {
        try
        {
            var runner = (HarnessMockerRunner)MockerBootstrap.New<HarnessMockerRunner>(["template", configPath, "--no-env"]);
            runner.Run();
            var exitCode = runner.LastExitCode ?? Environment.ExitCode;
            return new ValidationOutcome(
                exitCode == 0,
                exitCode,
                BuildSummary(stdout, stderr),
                stdout.ToString() + stderr.ToString());
        }
        catch (Exception exception)
        {
            return Failure(exception, stdout, stderr);
        }
    }

    private static ValidationOutcome Failure(Exception exception, StringWriter stdout, StringWriter stderr)
    {
        var summary = $"{exception.GetType().Name}: {exception.Message}";
        return new ValidationOutcome(
            false,
            Environment.ExitCode,
            summary,
            stdout.ToString() + stderr.ToString() + Environment.NewLine + exception);
    }

    private static string BuildSummary(StringWriter stdout, StringWriter stderr)
    {
        var output = string.Join(Environment.NewLine,
            new[] { stdout.ToString(), stderr.ToString() }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.IsNullOrWhiteSpace(output) ? "No output captured." : SweepWorkspace.Truncate(output.Trim(), 400);
    }

    private sealed class HarnessMockerRunner : QaaS.Mocker.MockerRunner
    {
        public HarnessMockerRunner(QaaS.Mocker.ExecutionBuilder? executionBuilder, Action<int>? exitAction = null)
            : base(executionBuilder, exitAction)
        {
        }

        public int? LastExitCode { get; private set; }

        protected override void ExitProcess(int exitCode)
        {
            LastExitCode = exitCode;
            Environment.ExitCode = exitCode;
        }

        protected override void SetProcessExitCode(int exitCode)
        {
            LastExitCode = exitCode;
            Environment.ExitCode = exitCode;
        }
    }
}
