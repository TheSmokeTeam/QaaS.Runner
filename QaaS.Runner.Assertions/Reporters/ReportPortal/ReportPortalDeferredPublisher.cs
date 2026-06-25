using Microsoft.Extensions.Logging;
using QaaS.Framework.Configurations.CustomExceptions;
using ReportPortal.Client;
using ReportPortal.Client.Abstractions;
using ReportPortal.Client.Abstractions.Models;
using ReportPortal.Client.Abstractions.Requests;

namespace QaaS.Runner.Assertions.Reporters.ReportPortal;

/// <summary>
/// Validates and publishes queued ReportPortal reporter results without holding a ReportPortal client during execution.
/// </summary>
internal interface IReportPortalDeferredPublisher
{
    Task ValidateAsync(IEnumerable<ReportPortalReporter> reporters, ILogger logger,
        CancellationToken cancellationToken = default);

    Task PublishAsync(IEnumerable<ReportPortalReporter> reporters, ILogger logger,
        CancellationToken cancellationToken = default);
}

internal sealed class ReportPortalDeferredPublisher : IReportPortalDeferredPublisher, IDisposable
{
    private readonly ReportPortalAccessValidator _accessValidator;
    private readonly IReportPortalClientFactory _clientFactory;
    private readonly DateTimeOffset _startedAtLocal;
    private readonly Dictionary<string, ReportPortalAccessResult> _validatedAccessByGroup = new(StringComparer.Ordinal);
    private bool _disposed;

    public ReportPortalDeferredPublisher()
        : this(new ReportPortalAccessValidator(), new ReportPortalClientFactory(), DateTimeOffset.Now)
    {
    }

    internal ReportPortalDeferredPublisher(
        ReportPortalAccessValidator accessValidator,
        IReportPortalClientFactory clientFactory,
        DateTimeOffset startedAtLocal)
    {
        _accessValidator = accessValidator;
        _clientFactory = clientFactory;
        _startedAtLocal = startedAtLocal;
    }

    public async Task ValidateAsync(IEnumerable<ReportPortalReporter> reporters, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reporters);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            var launchPlans = ReportPortalLaunchPlan.Build(reporters, _startedAtLocal, requireQueuedResults: false);
            if (launchPlans.Count == 0)
            {
                logger.LogDebug("ReportPortal validation skipped because no enabled ReportPortal reporters were built.");
                return;
            }

            var failures = new List<string>();
            foreach (var launchPlan in launchPlans)
            {
                var accessResult = await _accessValidator.EnsureWriteAccessAsync(launchPlan, logger, cancellationToken)
                    .ConfigureAwait(false);
                _validatedAccessByGroup[launchPlan.GroupKey] = accessResult;

                if (!CanPublish(accessResult))
                    failures.Add(accessResult.FailureReason ?? "ReportPortal publishing is disabled for this launch group.");
            }

            if (failures.Count == 0)
            {
                logger.LogInformation("Validated ReportPortal access for {LaunchGroupCount} launch group(s).",
                    launchPlans.Count);
                return;
            }

            throw new InvalidConfigurationsException(
                "ReportPortal validation failed before execution started." + Environment.NewLine +
                string.Join(Environment.NewLine, failures.Distinct(StringComparer.Ordinal)));
        }
        finally
        {
            _accessValidator.Dispose();
        }
    }

    public Task PublishAsync(IEnumerable<ReportPortalReporter> reporters, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reporters);
        ArgumentNullException.ThrowIfNull(logger);

        var launchPlans = ReportPortalLaunchPlan.Build(reporters, _startedAtLocal, requireQueuedResults: true);
        if (launchPlans.Count == 0)
        {
            logger.LogDebug("ReportPortal final publish skipped because no assertion results were queued.");
            return Task.CompletedTask;
        }

        foreach (var launchPlan in launchPlans)
        {
            PublishLaunch(launchPlan, logger, cancellationToken);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _accessValidator.Dispose();
    }

    private void PublishLaunch(ReportPortalLaunchPlan launchPlan, ILogger logger, CancellationToken cancellationToken)
    {
        if (!_validatedAccessByGroup.TryGetValue(launchPlan.GroupKey, out var accessResult) ||
            !CanPublish(accessResult))
        {
            logger.LogWarning(
                "Skipping ReportPortal publish for project {ProjectName} and system {SystemName} because the launch group was not validated successfully.",
                launchPlan.Project ?? "<missing-project>",
                launchPlan.System);
            return;
        }

        IClientService? service = null;
        string? launchUuid = null;
        try
        {
            service = _clientFactory.Create(accessResult.EndpointUri!, accessResult.Project!, accessResult.ApiKey!);
            var launchStartTimeUtc = DateTime.UtcNow;
            var launch = service.Launch.StartAsync(new StartLaunchRequest
            {
                Name = launchPlan.LaunchName,
                Description = launchPlan.Description,
                Mode = launchPlan.DebugMode ? LaunchMode.Debug : LaunchMode.Default,
                StartTime = launchStartTimeUtc,
                Attributes = launchPlan.BuildLaunchAttributes()
            }, cancellationToken).GetAwaiter().GetResult();

            launchUuid = launch.Uuid;
            logger.LogInformation(
                "Started ReportPortal launch {LaunchUuid} in project {ProjectName} for system {SystemName}.",
                launchUuid, accessResult.Project, launchPlan.System);

            var publishContext = new ReportPortalPublishContext(service, launchUuid, launchStartTimeUtc, launchPlan);
            foreach (var reporterResults in launchPlan.ReporterResults)
            {
                reporterResults.Reporter.PublishQueuedResults(publishContext, reporterResults.Results, logger);
            }

            try
            {
                service.Launch.FinishAsync(launchUuid, new FinishLaunchRequest
                {
                    EndTime = DateTime.UtcNow
                }, cancellationToken).GetAwaiter().GetResult();
                logger.LogInformation(
                    "Finished ReportPortal launch {LaunchUuid} in project {ProjectName} for system {SystemName}.",
                    launchUuid, accessResult.Project, launchPlan.System);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Could not finish ReportPortal launch {LaunchUuid} in project {ProjectName}.",
                    launchUuid, accessResult.Project);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Could not publish ReportPortal launch {LaunchUuid} for project {ProjectName} and system {SystemName}.",
                launchUuid ?? "<not-started>",
                accessResult.Project ?? launchPlan.Project ?? "<missing-project>",
                launchPlan.System);
        }
        finally
        {
            if (service is IDisposable disposableService)
            {
                try
                {
                    disposableService.Dispose();
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception,
                        "Could not dispose ReportPortal client service for project {ProjectName} and system {SystemName}.",
                        accessResult.Project ?? launchPlan.Project ?? "<missing-project>",
                        launchPlan.System);
                }
            }
        }
    }

    private static bool CanPublish(ReportPortalAccessResult accessResult)
    {
        return accessResult.CanPublish &&
               accessResult.EndpointUri is not null &&
               !string.IsNullOrWhiteSpace(accessResult.Project) &&
               !string.IsNullOrWhiteSpace(accessResult.ApiKey);
    }
}

internal interface IReportPortalClientFactory
{
    IClientService Create(Uri endpointUri, string project, string apiKey);
}

internal sealed class ReportPortalClientFactory : IReportPortalClientFactory
{
    public IClientService Create(Uri endpointUri, string project, string apiKey)
    {
        return new Service(endpointUri, project, apiKey);
    }
}

internal sealed record ReportPortalPublishContext(
    IClientService Service,
    string LaunchUuid,
    DateTime LaunchStartTimeUtc,
    ReportPortalLaunchPlan LaunchPlan);
