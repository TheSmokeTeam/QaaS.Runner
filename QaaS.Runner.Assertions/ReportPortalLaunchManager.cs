using Microsoft.Extensions.Logging;
using ReportPortal.Client;
using ReportPortal.Client.Abstractions;
using ReportPortal.Client.Abstractions.Models;
using ReportPortal.Client.Abstractions.Requests;
using QaaS.Runner.Assertions.ConfigurationObjects;

namespace QaaS.Runner.Assertions;

/// <summary>
/// Owns the single ReportPortal launch used by one runner invocation. The manager starts the launch lazily
/// when the first assertion is reported, reuses that launch for every later assertion, and finishes it during teardown.
/// </summary>
public sealed class ReportPortalLaunchManager : IDisposable
{
    private readonly SemaphoreSlim _launchLock = new(1, 1);
    private readonly ReportPortalProvisioningClient _provisioningClient;
    private Service? _service;
    private string? _launchUuid;
    private ReportPortalSettings? _settings;
    private DateTime _launchStartTimeUtc;
    private bool _launchFinished;
    private bool _disposed;

    public ReportPortalLaunchManager() : this(new ReportPortalProvisioningClient())
    {
    }

    internal ReportPortalLaunchManager(ReportPortalProvisioningClient provisioningClient)
    {
        _provisioningClient = provisioningClient;
    }

    /// <summary>
    /// Starts the shared ReportPortal launch on first use and returns the launch context required by reporters.
    /// Subsequent calls reuse the same launch and verify that the requested settings are compatible.
    /// </summary>
    /// <param name="settings">The resolved ReportPortal settings for the current execution.</param>
    /// <param name="logger">Logger used for lifecycle diagnostics.</param>
    /// <param name="cancellationToken">Cancellation token for the remote API call.</param>
    /// <returns>
    /// The active launch context, or <see langword="null" /> when ReportPortal publishing is disabled for this run.
    /// </returns>
    internal async Task<ReportPortalLaunchContext?> EnsureLaunchStartedAsync(ReportPortalSettings settings,
        ILogger logger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        if (!settings.Enabled)
            return null;

        await _launchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_launchUuid is not null && _service is not null)
            {
                EnsureCompatibleSettings(settings);
                return new ReportPortalLaunchContext(_service, _launchUuid);
            }

            settings.Validate();
            _settings = settings;
            _launchStartTimeUtc = DateTime.UtcNow;
            var provisioningResult = await _provisioningClient
                .EnsureProjectAccessAsync(settings, logger, cancellationToken)
                .ConfigureAwait(false);
            _service = new Service(settings.EndpointUri, provisioningResult.Project, provisioningResult.ApiKey);

            try
            {
                var launch = await _service.Launch.StartAsync(new StartLaunchRequest
                {
                    Name = settings.LaunchName,
                    Description = settings.Description,
                    Mode = settings.DebugMode ? LaunchMode.Debug : LaunchMode.Default,
                    StartTime = _launchStartTimeUtc,
                    Attributes = settings.BuildLaunchAttributes()
                }, cancellationToken).ConfigureAwait(false);

                _launchUuid = launch.Uuid;
                logger.LogInformation(
                    "Started ReportPortal launch {LaunchUuid} in project {ProjectName} using endpoint {Endpoint}",
                    _launchUuid, provisioningResult.Project, settings.Endpoint);

                return new ReportPortalLaunchContext(_service, _launchUuid);
            }
            catch
            {
                _service.Dispose();
                _service = null;
                _settings = null;
                _launchUuid = null;
                _launchStartTimeUtc = default;
                throw;
            }
        }
        finally
        {
            _launchLock.Release();
        }
    }

    /// <summary>
    /// Finishes the shared ReportPortal launch if one was started. This is called from runner teardown so the launch
    /// closes cleanly before any optional Allure serving step blocks the process.
    /// </summary>
    /// <param name="logger">Logger used for lifecycle diagnostics.</param>
    /// <param name="cancellationToken">Cancellation token for the remote API call.</param>
    public async Task FinishLaunchAsync(ILogger logger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logger);

        await _launchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_launchFinished || _launchUuid is null || _service is null)
            {
                logger.LogDebug("ReportPortal launch finalization skipped because no launch is active.");
                return;
            }

            await _service.Launch.FinishAsync(_launchUuid, new FinishLaunchRequest
            {
                EndTime = DateTime.UtcNow
            }, cancellationToken).ConfigureAwait(false);

            _launchFinished = true;
            logger.LogInformation("Finished ReportPortal launch {LaunchUuid}", _launchUuid);
        }
        finally
        {
            _launchLock.Release();
        }
    }

    private void EnsureCompatibleSettings(ReportPortalSettings settings)
    {
        if (_settings is null || _settings.IsCompatibleWith(settings))
            return;

        throw new InvalidOperationException(
            "Multiple executions in the same runner invocation attempted to publish to different ReportPortal launches. " +
            "Use one consistent ReportPortal configuration per runner invocation.");
    }

    /// <summary>
    /// Releases the underlying client resources owned by the launch manager.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _launchLock.Dispose();
        _service?.Dispose();
        _provisioningClient.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Lightweight runtime context shared by reporters after the launch has started.
/// </summary>
/// <param name="Service">The active ReportPortal client service.</param>
/// <param name="LaunchUuid">The UUID of the live launch.</param>
internal sealed record ReportPortalLaunchContext(IClientService Service, string LaunchUuid);
