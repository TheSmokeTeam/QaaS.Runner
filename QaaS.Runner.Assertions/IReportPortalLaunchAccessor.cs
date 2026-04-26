using ReportPortal.Client.Abstractions;

namespace QaaS.Runner.Assertions;

/// <summary>
/// Exposes the active ReportPortal launch state to reporters.
/// </summary>
public interface IReportPortalLaunchAccessor
{
    public bool IsEnabled { get; }

    public bool IsActive { get; }

    public string? LaunchUuid { get; }

    public IClientService Client { get; }
}
