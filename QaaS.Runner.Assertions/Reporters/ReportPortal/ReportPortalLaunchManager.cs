using Microsoft.Extensions.Logging;
using ReportPortal.Client;
using ReportPortal.Client.Abstractions;
using ReportPortal.Client.Abstractions.Models;
using ReportPortal.Client.Abstractions.Requests;

namespace QaaS.Runner.Assertions.Reporters.ReportPortal;

/// <summary>
/// Owns the ReportPortal launches used by one runner invocation. QaaS opens at most one launch per
/// endpoint/project/system combination and reuses it across all matching assertions in the invocation.
/// </summary>
/// <remarks>
/// Launch creation and finalization are best-effort. Failures are logged as warnings and do not change the runner exit
/// code.
/// </remarks>
public sealed class ReportPortalLaunchManager : IDisposable
{
    private readonly SemaphoreSlim _launchLock = new(1, 1);
    private readonly ReportPortalAccessValidator _accessValidator;
    private readonly Dictionary<string, ManagedLaunch> _launches = new(StringComparer.Ordinal);
    private readonly HashSet<string> _suppressedLaunchKeys = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>
    /// Creates a launch manager with the default ReportPortal access validator.
    /// </summary>
    public ReportPortalLaunchManager() : this(new ReportPortalAccessValidator())
    {
    }

    /// <summary>
    /// Creates a launch manager with a caller-provided access validator.
    /// </summary>
    /// <param name="accessValidator">The validator used before a ReportPortal launch is opened.</param>
    internal ReportPortalLaunchManager(ReportPortalAccessValidator accessValidator)
    {
        _accessValidator = accessValidator;
    }

    /// <summary>
    /// Starts or reuses the shared launch for the resolved ReportPortal project and system. When the minimal access
    /// checks fail or launch creation fails, the manager logs a warning and returns <see langword="null" /> so the
    /// caller can keep the run green.
    /// </summary>
    /// <param name="settings">The resolved ReportPortal settings for the current launch group.</param>
    /// <param name="logger">The logger used for launch lifecycle messages.</param>
    /// <param name="cancellationToken">A cancellation token for ReportPortal client calls.</param>
    /// <returns>The active launch context, or <see langword="null" /> when publishing should be skipped.</returns>
    internal async Task<ReportPortalLaunchContext?> EnsureLaunchStartedAsync(ReportPortalSettings settings,
        ILogger logger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        if (!settings.Enabled)
            return null;

        var accessResult = await _accessValidator.EnsureWriteAccessAsync(settings, logger, cancellationToken)
            .ConfigureAwait(false);
        if (!CanPublish(accessResult))
            return null;

        var launchKey = settings.BuildLaunchGroupKey(accessResult.Project!, accessResult.EndpointUri!);

        await _launchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_launches.TryGetValue(launchKey, out var existingLaunch))
                return existingLaunch.ToContext();

            if (_suppressedLaunchKeys.Contains(launchKey))
                return null;

            IClientService service = new Service(accessResult.EndpointUri!, accessResult.Project!, accessResult.ApiKey!);
            var launchStartTimeUtc = DateTime.UtcNow;

            try
            {
                var launch = await service.Launch.StartAsync(new StartLaunchRequest
                {
                    Name = settings.LaunchName,
                    Description = settings.Description,
                    Mode = settings.DebugMode ? LaunchMode.Debug : LaunchMode.Default,
                    StartTime = launchStartTimeUtc,
                    Attributes = settings.BuildLaunchAttributes()
                }, cancellationToken).ConfigureAwait(false);

                var managedLaunch = new ManagedLaunch(
                    accessResult.Project!,
                    settings.System,
                    launchStartTimeUtc,
                    service,
                    launch.Uuid);
                _launches[launchKey] = managedLaunch;

                logger.LogInformation(
                    "Started ReportPortal launch {LaunchUuid} in project {ProjectName} for system {SystemName}.",
                    launch.Uuid, accessResult.Project!, settings.System);

                return managedLaunch.ToContext();
            }
            catch (Exception exception)
            {
                if (service is IDisposable disposableService)
                    disposableService.Dispose();
                _suppressedLaunchKeys.Add(launchKey);
                logger.LogWarning(exception,
                    "Could not start ReportPortal launch for project {ProjectName} and system {SystemName}. ReportPortal publishing will be skipped for this launch group.",
                    accessResult.Project!, settings.System);
                return null;
            }
        }
        finally
        {
            _launchLock.Release();
        }
    }

    /// <summary>
    /// Finishes every launch that was started during the invocation. Launch finalization is best-effort and never throws.
    /// </summary>
    /// <param name="logger">The logger used for finalization messages.</param>
    /// <param name="cancellationToken">A cancellation token for ReportPortal client calls.</param>
    public async Task FinishLaunchAsync(ILogger logger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logger);

        await _launchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_launches.Count == 0)
            {
                logger.LogDebug("ReportPortal launch finalization skipped because no launches were started.");
                return;
            }

            foreach (var launch in _launches.Values.Where(launch => !launch.IsFinished))
            {
                try
                {
                    await launch.Service.Launch.FinishAsync(launch.LaunchUuid, new FinishLaunchRequest
                    {
                        EndTime = DateTime.UtcNow
                    }, cancellationToken).ConfigureAwait(false);
                    launch.IsFinished = true;
                    logger.LogInformation(
                        "Finished ReportPortal launch {LaunchUuid} in project {ProjectName} for system {SystemName}.",
                        launch.LaunchUuid, launch.Project, launch.System);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception,
                        "Could not finish ReportPortal launch {LaunchUuid} in project {ProjectName}.",
                        launch.LaunchUuid, launch.Project);
                }
            }
        }
        finally
        {
            _launchLock.Release();
        }
    }

    /// <summary>
    /// Releases ReportPortal client services and the access validator.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _launchLock.Dispose();
        foreach (var launch in _launches.Values)
        {
            if (launch.Service is IDisposable disposableService)
                disposableService.Dispose();
        }
        _accessValidator.Dispose();
        GC.SuppressFinalize(this);
    }

    private static bool CanPublish(ReportPortalAccessResult accessResult)
    {
        return accessResult.CanPublish &&
               accessResult.EndpointUri is not null &&
               !string.IsNullOrWhiteSpace(accessResult.Project) &&
               !string.IsNullOrWhiteSpace(accessResult.ApiKey);
    }

    /// <summary>
    /// Tracks an open ReportPortal launch and the client service that owns it.
    /// </summary>
    private sealed class ManagedLaunch(
        string project,
        string system,
        DateTime launchStartTimeUtc,
        IClientService service,
        string launchUuid)
    {
        public string Project { get; } = project;
        public string System { get; } = system;
        public DateTime LaunchStartTimeUtc { get; } = launchStartTimeUtc;
        public IClientService Service { get; } = service;
        public string LaunchUuid { get; } = launchUuid;
        public bool IsFinished { get; set; }

        /// <summary>
        /// Creates the lightweight context used by reporters to publish test items.
        /// </summary>
        /// <returns>The launch context for the active ReportPortal launch.</returns>
        public ReportPortalLaunchContext ToContext()
        {
            return new ReportPortalLaunchContext(Service, LaunchUuid, LaunchStartTimeUtc);
        }
    }
}

/// <summary>
/// Lightweight runtime context shared by reporters after the launch has started.
/// </summary>
/// <param name="Service">The active ReportPortal client service.</param>
/// <param name="LaunchUuid">The UUID of the live launch.</param>
/// <param name="LaunchStartTimeUtc">The UTC timestamp used when the launch was started in ReportPortal.</param>
internal sealed record ReportPortalLaunchContext(IClientService Service, string LaunchUuid, DateTime LaunchStartTimeUtc);
