using System.IO.Abstractions;
using Autofac;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Runner.Assertions.AssertionObjects;

namespace QaaS.Runner.Assertions.Reporters;

public sealed class ReporterFactory(ILifetimeScope scope)
{
    public IReadOnlyList<IReporter> BuildReporters(
        IEnumerable<Assertion> assertions,
        Context context,
        DateTime testSuiteStartTimeUtc,
        IFileSystem? fileSystem = null)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        ArgumentNullException.ThrowIfNull(context);

        var targets = assertions
            .SelectMany(assertion => assertion.ReporterTargets)
            .Distinct()
            .ToArray();

        if (targets.Length == 0)
            return [];

        var resolvedFileSystem = fileSystem ?? new FileSystem();
        var reporters = new List<IReporter>(targets.Length);

        foreach (var target in targets)
        {
            switch (target)
            {
                case ReporterTarget.Allure:
                    reporters.Add(BuildReporter<AllureReporter>(
                        target,
                        context,
                        testSuiteStartTimeUtc,
                        resolvedFileSystem));
                    break;

                case ReporterTarget.ReportPortal:
                    reporters.Add(BuildReporter<ReportPortalReporter>(
                        target,
                        context,
                        testSuiteStartTimeUtc,
                        resolvedFileSystem));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Reporter target {target} is not mapped to a reporter type.");
            }
        }

        return reporters;
    }

    private IReporter BuildReporter<TReporter>(
        ReporterTarget target,
        Context context,
        DateTime testSuiteStartTimeUtc,
        IFileSystem fileSystem)
        where TReporter : class, IReporter
    {
        if (!scope.IsRegistered<TReporter>())
        {
            throw new InvalidOperationException(
                $"Reporter type {typeof(TReporter).FullName} mapped to target {target} is not registered in Autofac.");
        }

        var reporter = scope.Resolve<TReporter>();

        reporter.Target = target;
        reporter.Name = typeof(TReporter).Name;
        reporter.AssertionName = string.Empty;
        reporter.TestSuiteStartTimeUtc = testSuiteStartTimeUtc;

        if (reporter is BaseReporter baseReporter)
        {
            baseReporter.Context = context;
            baseReporter.FileSystem = fileSystem;
        }

        return reporter;
    }
}
