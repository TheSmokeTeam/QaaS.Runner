namespace QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;

/// <summary>
/// Configuration for Allure reporting. This reporter is enabled by default in the runner and can be used without any configuration, but this class allows to configure some of its behavior if needed.
/// </summary>
public record AllureConfig : IReporterConfig;