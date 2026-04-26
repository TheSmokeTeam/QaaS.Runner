namespace QaaS.Runner.ConfigurationObjects;

public sealed class ReportPortalConfig
{
    public bool Enabled { get; set; } = true;

    public ReportPortalServerConfig Server { get; set; } = new();

    public ReportPortalLaunchConfig Launch { get; set; } = new();
}

public sealed class ReportPortalServerConfig
{
    public string Url { get; set; } = string.Empty;

    public string Project { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;
}

public sealed class ReportPortalLaunchConfig
{
    public string Name { get; set; } = "QaaS Run";

    public string Description { get; set; } = "QaaS runner execution";
}
