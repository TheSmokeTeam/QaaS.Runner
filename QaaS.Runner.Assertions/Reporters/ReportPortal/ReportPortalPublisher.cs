using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Configurations.CustomExceptions;
using ReportPortal.Client;
using ReportPortal.Client.Abstractions;
using ReportPortal.Client.Abstractions.Models;
using ReportPortal.Client.Abstractions.Requests;

namespace QaaS.Runner.Assertions.Reporters.ReportPortal;

/// <summary>
/// Validates ReportPortal access and publishes queued reporter results without holding a ReportPortal client during execution.
/// </summary>
internal interface IReportPortalPublisher
{
    Task ValidateAsync(IEnumerable<ReportPortalReporter> reporters, ILogger logger,
        CancellationToken cancellationToken = default);

    Task PublishAsync(IEnumerable<ReportPortalReporter> reporters, ILogger logger,
        CancellationToken cancellationToken = default);
}

internal sealed class ReportPortalPublisher : IReportPortalPublisher, IDisposable
{
    private readonly HttpClient _validationHttpClient;
    private readonly bool _ownsValidationHttpClient;
    private readonly IReportPortalClientFactory _clientFactory;
    private readonly DateTimeOffset _startedAtLocal;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly ConcurrentDictionary<string, Lazy<Task<ReportPortalValidationResult>>> _validationCache =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReportPortalValidationResult> _validatedAccessByGroup =
        new(StringComparer.Ordinal);
    private bool _validationHttpClientDisposed;

    public ReportPortalPublisher()
        : this(new HttpClient(), ownsValidationHttpClient: true, new ReportPortalClientFactory(), DateTimeOffset.Now)
    {
    }

    internal ReportPortalPublisher(
        HttpClient validationHttpClient,
        IReportPortalClientFactory clientFactory,
        DateTimeOffset startedAtLocal)
        : this(validationHttpClient, ownsValidationHttpClient: false, clientFactory, startedAtLocal)
    {
    }

    private ReportPortalPublisher(
        HttpClient validationHttpClient,
        bool ownsValidationHttpClient,
        IReportPortalClientFactory clientFactory,
        DateTimeOffset startedAtLocal)
    {
        _validationHttpClient = validationHttpClient;
        _ownsValidationHttpClient = ownsValidationHttpClient;
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
                var validationResult = await EnsurePublishAccessAsync(launchPlan, logger, cancellationToken)
                    .ConfigureAwait(false);
                _validatedAccessByGroup[launchPlan.GroupKey] = validationResult;

                if (!CanPublish(validationResult))
                    failures.Add(validationResult.FailureReason ??
                                 "ReportPortal publishing is disabled for this launch group.");
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
            DisposeValidationHttpClient();
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
        DisposeValidationHttpClient();
    }

    private Task<ReportPortalValidationResult> EnsurePublishAccessAsync(ReportPortalLaunchPlan launchPlan,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildValidationCacheKey(launchPlan);
        var lazyResult = _validationCache.GetOrAdd(cacheKey,
            _ => new Lazy<Task<ReportPortalValidationResult>>(
                () => ValidateCoreAsync(launchPlan, logger, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyResult.Value;
    }

    private async Task<ReportPortalValidationResult> ValidateCoreAsync(ReportPortalLaunchPlan launchPlan,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(launchPlan.Project))
            return WarnAndReturnValidationFailure(logger,
                "Could not publish results to ReportPortal because ReportPortal.Project or MetaData.Team was not configured.");

        var projectName = launchPlan.Project;

        if (!launchPlan.TryGetEndpointUri(out var endpointUri, out var endpointFailureReason))
            return WarnAndReturnValidationFailure(logger,
                $"Could not publish results to ReportPortal: {endpointFailureReason}");

        if (string.IsNullOrWhiteSpace(launchPlan.ApiKey))
            return WarnAndReturnValidationFailure(logger,
                $"Could not publish results to ReportPortal project `{projectName}` because ReportPortal.ApiKey was not configured.");

        var apiKey = launchPlan.ApiKey;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri(endpointUri!, $"v1/project/{Uri.EscapeDataString(projectName)}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _validationHttpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return WarnAndReturnValidationFailure(logger,
                    $"Could not publish results to ReportPortal because the configured API key was rejected for project `{projectName}`.");

            if (!response.IsSuccessStatusCode)
                return WarnAndReturnValidationFailure(logger,
                    response.StatusCode == HttpStatusCode.NotFound
                        ? $"Could not publish results to ReportPortal because no accessible project matches `{projectName}`."
                        : $"Could not publish results to ReportPortal at {endpointUri} for project `{projectName}`. Status={(int)response.StatusCode} {response.ReasonPhrase}. Response={responseBody}");

            var project = JsonSerializer.Deserialize<ProjectResponse>(responseBody, _jsonSerializerOptions)
                          ?? new ProjectResponse();

            if (string.IsNullOrWhiteSpace(project.ProjectName))
                return WarnAndReturnValidationFailure(logger,
                    $"Could not publish results to ReportPortal because the endpoint `{launchPlan.Endpoint}` returned an unreadable project payload for project `{projectName}`.");

            return ReportPortalValidationResult.Success(endpointUri!, project.ProjectName, apiKey);
        }
        catch (TaskCanceledException)
        {
            return WarnAndReturnValidationFailure(logger,
                $"Could not publish results to ReportPortal because the endpoint `{launchPlan.Endpoint}` timed out.");
        }
        catch (HttpRequestException exception)
        {
            return WarnAndReturnValidationFailure(logger,
                $"Could not publish results to ReportPortal because the endpoint `{launchPlan.Endpoint}` is unreachable. {exception.Message}");
        }
        catch (JsonException exception)
        {
            return WarnAndReturnValidationFailure(logger,
                $"Could not publish results to ReportPortal because the endpoint `{launchPlan.Endpoint}` returned an unreadable project payload. {exception.Message}");
        }
    }

    private void PublishLaunch(ReportPortalLaunchPlan launchPlan, ILogger logger, CancellationToken cancellationToken)
    {
        if (!_validatedAccessByGroup.TryGetValue(launchPlan.GroupKey, out var validationResult) ||
            !CanPublish(validationResult))
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
            service = _clientFactory.Create(validationResult.EndpointUri!, validationResult.Project!,
                validationResult.ApiKey!);
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
                launchUuid, validationResult.Project, launchPlan.System);

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
                    launchUuid, validationResult.Project, launchPlan.System);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Could not finish ReportPortal launch {LaunchUuid} in project {ProjectName}.",
                    launchUuid, validationResult.Project);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Could not publish ReportPortal launch {LaunchUuid} for project {ProjectName} and system {SystemName}.",
                launchUuid ?? "<not-started>",
                validationResult.Project ?? launchPlan.Project ?? "<missing-project>",
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
                        validationResult.Project ?? launchPlan.Project ?? "<missing-project>",
                        launchPlan.System);
                }
            }
        }
    }

    private static string BuildValidationCacheKey(ReportPortalLaunchPlan launchPlan)
    {
        var endpoint = launchPlan.TryGetEndpointUri(out var endpointUri, out _)
            ? endpointUri!.AbsoluteUri
            : launchPlan.Endpoint ?? "<missing-endpoint>";

        return string.Join("::",
            endpoint,
            launchPlan.ApiKey ?? "<missing-api-key>",
            launchPlan.Project ?? "<missing-project>",
            launchPlan.System);
    }

    private static bool CanPublish(ReportPortalValidationResult validationResult)
    {
        return validationResult.CanPublish &&
               validationResult.EndpointUri is not null &&
               !string.IsNullOrWhiteSpace(validationResult.Project) &&
               !string.IsNullOrWhiteSpace(validationResult.ApiKey);
    }

    private static ReportPortalValidationResult WarnAndReturnValidationFailure(ILogger logger, string warningMessage)
    {
        logger.LogWarning(warningMessage);
        return ReportPortalValidationResult.Failure(warningMessage);
    }

    private void DisposeValidationHttpClient()
    {
        if (_validationHttpClientDisposed || !_ownsValidationHttpClient)
            return;

        _validationHttpClientDisposed = true;
        _validationHttpClient.Dispose();
    }

    private sealed class ProjectResponse
    {
        public string? ProjectName { get; init; }
    }

    private sealed class ReportPortalValidationResult
    {
        private ReportPortalValidationResult(bool canPublish, Uri? endpointUri, string? project, string? apiKey,
            string? failureReason)
        {
            CanPublish = canPublish;
            EndpointUri = endpointUri;
            Project = project;
            ApiKey = apiKey;
            FailureReason = failureReason;
        }

        public bool CanPublish { get; }
        public Uri? EndpointUri { get; }
        public string? Project { get; }
        public string? ApiKey { get; }
        public string? FailureReason { get; }

        public static ReportPortalValidationResult Success(Uri endpointUri, string project, string apiKey)
        {
            return new ReportPortalValidationResult(true, endpointUri, project, apiKey, null);
        }

        public static ReportPortalValidationResult Failure(string failureReason)
        {
            return new ReportPortalValidationResult(false, null, null, null, failureReason);
        }
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
