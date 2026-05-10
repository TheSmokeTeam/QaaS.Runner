using System.IO.Abstractions;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.Assertions.Reporters.Allure;
using QaaS.Runner.Assertions.Reporters.ReportPortal;

namespace QaaS.Runner.Assertions.Reporters;

internal static class ReporterFactory
{
    internal static IList<IReporter> CreateReporters(Context context, DateTime testSuiteStartTimeUtc,
        ReportPortalSettings? reportPortalSettings = null,
        ReportPortalLaunchManager? reportPortalLaunchManager = null,
        IFileSystem? fileSystem = null)
    {
        var reporters = new List<IReporter>();

        foreach (var reporterTarget in Enum.GetValues<ReporterTarget>())
        {
            switch (reporterTarget)
            {
                case ReporterTarget.Allure:
                    reporters.Add(CreateAllureReporter(context, testSuiteStartTimeUtc, fileSystem));
                    break;
                case ReporterTarget.ReportPortal:
                    if (reportPortalSettings is { Enabled: true } && reportPortalLaunchManager is not null)
                    {
                        reporters.Add(CreateReportPortalReporter(context, testSuiteStartTimeUtc,
                            reportPortalSettings, reportPortalLaunchManager, fileSystem));
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(reporterTarget), reporterTarget,
                        "Unsupported reporter target.");
            }
        }

        return reporters;
    }

    private static AllureReporter CreateAllureReporter(Context context, DateTime testSuiteStartTimeUtc,
        IFileSystem? fileSystem)
    {
        return ApplyCommonProperties(
            new AllureReporter(),
            context,
            testSuiteStartTimeUtc,
            fileSystem,
            nameof(AllureReporter));
    }

    private static ReportPortalReporter CreateReportPortalReporter(Context context, DateTime testSuiteStartTimeUtc,
        ReportPortalSettings reportPortalSettings, ReportPortalLaunchManager reportPortalLaunchManager,
        IFileSystem? fileSystem)
    {
        return ApplyCommonProperties(
            new ReportPortalReporter
            {
                Settings = reportPortalSettings,
                LaunchManager = reportPortalLaunchManager
            },
            context,
            testSuiteStartTimeUtc,
            fileSystem,
            nameof(ReportPortalReporter));
    }

    private static TReporter ApplyCommonProperties<TReporter>(TReporter reporter, Context context,
        DateTime testSuiteStartTimeUtc, IFileSystem? fileSystem, string reporterName)
        where TReporter : BaseReporter
    {
        reporter.Name = reporterName;
        reporter.AssertionName = reporterName;
        reporter.Severity = AssertionObjects.AssertionSeverity.Normal;
        reporter.Context = context;
        reporter.EpochTestSuiteStartTime =
            new DateTimeOffset(testSuiteStartTimeUtc, TimeSpan.Zero).ToUnixTimeMilliseconds();
        reporter.FileSystem = fileSystem ?? new FileSystem();

        return reporter;
    }
}
