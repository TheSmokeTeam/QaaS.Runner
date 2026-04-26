namespace QaaS.Runner.Assertions;

internal static class ReporterFactory
{
    internal static BaseReporter Create(ReporterKind reporterKind)
    {
        return reporterKind switch
        {
            ReporterKind.Allure => new AllureReporter(),
            ReporterKind.ReportPortal => new ReportPortalReporter(),
            _ => throw new ArgumentOutOfRangeException(nameof(reporterKind), reporterKind,
                "Unsupported reporter kind.")
        };
    }
}
