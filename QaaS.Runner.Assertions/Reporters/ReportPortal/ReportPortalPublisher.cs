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
/// Runner-owned ReportPortal coordinator that validates access before reporting execution and publishes queued reporter results during cleanup.
/// </summary>
public class ReportPortalPublisher(ILogger logger) : IDisposable
{
    private readonly HttpClient _validationHttpClient = new();
    private readonly DateTimeOffset _startedAtLocal = DateTimeOffset.Now;
    private readonly HashSet<string> _validatedGroupKeys = new(StringComparer.Ordinal);
    private bool _validationHttpClientDisposed;
    internal string? TerminalOutputPath { get; set; }

    /// <summary>
    /// Validates every enabled ReportPortal launch group without creating launches or writing items.
    /// </summary>
    /// <remarks>
    /// The runner calls this immediately before the first ReportPortal-enabled reporting execution. Configuration or
    /// access failures are reported as <see cref="InvalidConfigurationsException" /> so reporting commands do not start.
    /// </remarks>
    /// <param name="reporters">The ReportPortal reporters built for this runner invocation.</param>
    /// <param name="cancellationToken">A token that cancels read-only ReportPortal access checks.</param>
    public virtual async Task ValidateAsync(IEnumerable<ReportPortalReporter> reporters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reporters);

        try
        {
            await ValidateReportersAsync(reporters, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Dispose();
        }
    }

    /// <summary>
    /// Publishes queued ReportPortal assertion results for the launch groups that passed pre-run validation.
    /// </summary>
    /// <remarks>
    /// The runner calls this during cleanup before execution scopes are disposed. A ReportPortal client is created only
    /// inside this method, and final publish failures are logged as errors without changing the assertion exit code.
    /// </remarks>
    /// <param name="reporters">The ReportPortal reporters that queued assertion results during execution.</param>
    /// <param name="cancellationToken">A token that cancels final ReportPortal publish operations.</param>
    public virtual async Task PublishAsync(IEnumerable<ReportPortalReporter> reporters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reporters);

        var launchPlans = ReportPortalLaunchPlan.Build(reporters, _startedAtLocal, requireQueuedResults: true);
        if (launchPlans.Count == 0)
        {
            logger.LogDebug("ReportPortal final publish skipped because no assertion results were queued.");
            return;
        }

        foreach (var launchPlan in launchPlans)
        {
            await PublishLaunch(launchPlan, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds validation launch plans and skips validation when no ReportPortal reporters are enabled.
    /// </summary>
    private async Task ValidateReportersAsync(IEnumerable<ReportPortalReporter> reporters,
        CancellationToken cancellationToken)
    {
        var launchPlans = ReportPortalLaunchPlan.Build(reporters, _startedAtLocal, requireQueuedResults: false);
        if (launchPlans.Count == 0)
        {
            logger.LogDebug("ReportPortal validation skipped because no enabled ReportPortal reporters were built.");
            return;
        }

        await ValidateLaunchPlansOrThrowAsync(launchPlans, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates every launch group and throws one combined configuration failure if any group cannot publish.
    /// </summary>
    private async Task ValidateLaunchPlansOrThrowAsync(IReadOnlyList<ReportPortalLaunchPlan> launchPlans,
        CancellationToken cancellationToken)
    {
        var failures = await CollectValidationFailuresAsync(launchPlans, cancellationToken).ConfigureAwait(false);
        if (failures.Count > 0)
        {
            throw new InvalidConfigurationsException(
                "ReportPortal validation failed before execution started." + Environment.NewLine +
                string.Join(Environment.NewLine, failures.Distinct(StringComparer.Ordinal)));
        }

        logger.LogInformation("Validated ReportPortal access for {LaunchGroupCount} launch group(s).",
            launchPlans.Count);
    }

    /// <summary>
    /// Validates all launch groups while collecting each configuration failure for a single runner error.
    /// </summary>
    private async Task<List<string>> CollectValidationFailuresAsync(IReadOnlyList<ReportPortalLaunchPlan> launchPlans,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        foreach (var launchPlan in launchPlans)
        {
            try
            {
                await ValidateLaunchPlanAsync(launchPlan, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidConfigurationsException exception)
            {
                failures.Add(exception.Message);
            }
        }

        return failures;
    }

    /// <summary>
    /// Validates one launch group and remembers successful access for cleanup-time publishing.
    /// </summary>
    private async Task ValidateLaunchPlanAsync(ReportPortalLaunchPlan launchPlan, CancellationToken cancellationToken)
    {
        await ValidateCoreAsync(launchPlan, cancellationToken).ConfigureAwait(false);
        _validatedGroupKeys.Add(launchPlan.GroupKey);
    }

    /// <summary>
    /// Checks the normalized endpoint, API key, and project visibility without creating a ReportPortal client service.
    /// </summary>
    private async Task ValidateCoreAsync(
        ReportPortalLaunchPlan launchPlan,
        CancellationToken cancellationToken)
    {
        var projectName = GetProjectName(launchPlan);
        var endpointUri = GetEndpointUri(launchPlan);
        var apiKey = GetApiKey(launchPlan, projectName);

        try
        {
            using var response = await SendProjectLookupAsync(endpointUri, projectName, apiKey, cancellationToken)
                .ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            EnsureProjectLookupSucceeded(response, responseBody, endpointUri, projectName);
            EnsureReadableProjectPayload(launchPlan, responseBody, projectName);
        }
        catch (TaskCanceledException)
        {
            var errorMessage =
                $"Could not publish results to ReportPortal because the endpoint `{launchPlan.Endpoint}` timed out.";
            logger.LogError(errorMessage);
            throw new InvalidConfigurationsException(errorMessage);
        }
        catch (HttpRequestException exception)
        {
            var errorMessage =
                $"Could not publish results to ReportPortal because the endpoint `{launchPlan.Endpoint}` is unreachable. {exception.Message}";
            logger.LogError(errorMessage);
            throw new InvalidConfigurationsException(errorMessage);
        }
        catch (JsonException exception)
        {
            var errorMessage =
                $"Could not publish results to ReportPortal because the endpoint `{launchPlan.Endpoint}` returned an unreadable project payload. {exception.Message}";
            logger.LogError(errorMessage);
            throw new InvalidConfigurationsException(errorMessage);
        }
    }

    private string GetProjectName(ReportPortalLaunchPlan launchPlan)
    {
        if (!string.IsNullOrWhiteSpace(launchPlan.Project))
            return launchPlan.Project;

        const string errorMessage =
            "Could not publish results to ReportPortal because ReportPortal.Project was configured as an empty value.";
        logger.LogError(errorMessage);
        throw new InvalidConfigurationsException(errorMessage);
    }

    private Uri GetEndpointUri(ReportPortalLaunchPlan launchPlan)
    {
        if (launchPlan.TryGetEndpointUri(out var endpointUri, out var endpointFailureReason))
            return endpointUri!;

        var errorMessage = $"Could not publish results to ReportPortal: {endpointFailureReason}";
        logger.LogError(errorMessage);
        throw new InvalidConfigurationsException(errorMessage);
    }

    private string GetApiKey(ReportPortalLaunchPlan launchPlan, string projectName)
    {
        if (!string.IsNullOrWhiteSpace(launchPlan.ApiKey))
            return launchPlan.ApiKey;

        var errorMessage =
            $"Could not publish results to ReportPortal project `{projectName}` because ReportPortal.ApiKey was not configured.";
        logger.LogError(errorMessage);
        throw new InvalidConfigurationsException(errorMessage);
    }

    /// <summary>
    /// Builds and sends the ReportPortal project lookup request used by read-only validation.
    /// </summary>
    private async Task<HttpResponseMessage> SendProjectLookupAsync(Uri endpointUri, string projectName, string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(endpointUri, $"v1/project/{Uri.EscapeDataString(projectName)}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return await SendValidationRequestAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends the read-only ReportPortal project lookup used during pre-run validation.
    /// </summary>
    protected virtual Task<HttpResponseMessage> SendValidationRequestAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return _validationHttpClient.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Converts ReportPortal project lookup HTTP failures into runner configuration failures.
    /// </summary>
    private void EnsureProjectLookupSucceeded(HttpResponseMessage response, string responseBody, Uri endpointUri,
        string projectName)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var errorMessage =
                $"Could not publish results to ReportPortal because the configured API key was rejected for project `{projectName}`.";
            logger.LogError(errorMessage);
            throw new InvalidConfigurationsException(errorMessage);
        }

        if (response.IsSuccessStatusCode)
            return;

        var failureMessage = response.StatusCode == HttpStatusCode.NotFound
            ? $"Could not publish results to ReportPortal because no accessible project matches `{projectName}`."
            : $"Could not publish results to ReportPortal at {endpointUri} for project `{projectName}`. Status={(int)response.StatusCode} {response.ReasonPhrase}. Response={responseBody}";
        logger.LogError(failureMessage);
        throw new InvalidConfigurationsException(failureMessage);
    }

    /// <summary>
    /// Verifies the project lookup response has the expected project payload shape.
    /// </summary>
    private void EnsureReadableProjectPayload(ReportPortalLaunchPlan launchPlan, string responseBody,
        string projectName)
    {
        using var projectPayload = JsonDocument.Parse(responseBody);
        JsonElement projectNameElement;
        var hasProjectName =
            projectPayload.RootElement.TryGetProperty("projectName", out projectNameElement) ||
            projectPayload.RootElement.TryGetProperty("ProjectName", out projectNameElement);
        var projectNameFromPayload = hasProjectName && projectNameElement.ValueKind == JsonValueKind.String
            ? projectNameElement.GetString()
            : null;

        if (!string.IsNullOrWhiteSpace(projectNameFromPayload))
            return;

        var errorMessage =
            $"Could not publish results to ReportPortal because the endpoint `{launchPlan.Endpoint}` returned an unreadable project payload for project `{projectName}`.";
        logger.LogError(errorMessage);
        throw new InvalidConfigurationsException(errorMessage);
    }

    /// <summary>
    /// Opens a short-lived ReportPortal client, writes one grouped launch, and disposes the client immediately after.
    /// </summary>
    private async Task PublishLaunch(ReportPortalLaunchPlan launchPlan, CancellationToken cancellationToken)
    {
        if (!TryGetPublishAccess(launchPlan, out var endpointUri, out var projectName, out var apiKey))
            return;

        await PublishLaunchWithClient(endpointUri, projectName, apiKey, launchPlan, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Rechecks that a launch group passed validation and still has the minimum publish configuration.
    /// </summary>
    private bool TryGetPublishAccess(ReportPortalLaunchPlan launchPlan, out Uri endpointUri, out string projectName,
        out string apiKey)
    {
        endpointUri = default!;
        projectName = launchPlan.Project;
        apiKey = string.Empty;

        if (!_validatedGroupKeys.Contains(launchPlan.GroupKey))
        {
            logger.LogError(
                "Skipping ReportPortal publish for project {ProjectName} and system {SystemName} because the launch group was not validated successfully.",
                launchPlan.Project,
                launchPlan.System);
            return false;
        }

        if (!launchPlan.TryGetEndpointUri(out var normalizedEndpointUri, out var endpointFailureReason) ||
            string.IsNullOrWhiteSpace(launchPlan.Project) ||
            string.IsNullOrWhiteSpace(launchPlan.ApiKey))
        {
            logger.LogError(
                "Skipping ReportPortal publish for project {ProjectName} and system {SystemName} because the launch group no longer has valid publish access. Reason={FailureReason}",
                launchPlan.Project,
                launchPlan.System,
                endpointFailureReason ?? "Missing ReportPortal project or API key.");
            return false;
        }

        endpointUri = normalizedEndpointUri!;
        projectName = launchPlan.Project;
        apiKey = launchPlan.ApiKey;
        return true;
    }

    /// <summary>
    /// Creates a short-lived ReportPortal client and owns all error-level final publish handling for one launch.
    /// </summary>
    private async Task PublishLaunchWithClient(Uri endpointUri, string projectName, string apiKey,
        ReportPortalLaunchPlan launchPlan, CancellationToken cancellationToken)
    {
        IClientService? service = null;
        string? launchUuid = null;
        try
        {
            service = CreateClient(endpointUri, projectName, apiKey);
            launchUuid = await StartLaunchAsync(service, launchPlan, projectName,
                cancellationToken).ConfigureAwait(false);

            await PublishTerminalOutput(service, launchPlan, launchUuid, cancellationToken).ConfigureAwait(false);
            await PublishLaunchItems(service, launchPlan, projectName, launchUuid, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Could not publish ReportPortal launch {LaunchUuid} for project {ProjectName} and system {SystemName}.",
                launchUuid ?? "<not-started>",
                projectName,
                launchPlan.System);
        }
        finally
        {
            DisposeService(service, projectName, launchPlan.System);
        }
    }

    /// <summary>
    /// Attaches the captured terminal output to a launch as <c>terminal.log</c> when saving is enabled.
    /// </summary>
    /// <remarks>Attachment failures are logged and do not interrupt assertion-result publishing.</remarks>
    /// <param name="service">The client service for the launch being published.</param>
    /// <param name="launchPlan">The launch plan containing reporter configuration and timing.</param>
    /// <param name="launchUuid">The identifier of the launch that receives the attachment.</param>
    /// <param name="cancellationToken">A token that cancels attachment creation.</param>
    private async Task PublishTerminalOutput(IClientService service, ReportPortalLaunchPlan launchPlan,
        string launchUuid, CancellationToken cancellationToken)
    {
        if (TerminalOutputPath is null || launchPlan.ReporterResults.All(result => result.Reporter.SaveTerminalOutput == false))
            return;
        try
        {
            var attachment = new LogItemAttach("text/plain",
                await File.ReadAllBytesAsync(TerminalOutputPath, cancellationToken).ConfigureAwait(false))
            {
                Name = "terminal.log"
            };
            await service.LogItem.CreateAsync(new CreateLogItemRequest
            {
                LaunchUuid = launchUuid,
                Level = global::ReportPortal.Client.Abstractions.Models.LogLevel.Info,
                Text = "Runner terminal output.",
                Time = launchPlan.LaunchEndTimeUtc,
                Attach = attachment
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) { logger.LogError(exception,
            "Could not attach terminal.log to ReportPortal launch {LaunchUuid}.", launchUuid); }
    }

    /// <summary>
    /// Creates the short-lived ReportPortal client used only during final publishing.
    /// </summary>
    protected virtual IClientService CreateClient(Uri endpointUri, string project, string apiKey)
    {
        return new Service(endpointUri, project, apiKey);
    }

    /// <summary>
    /// Starts a ReportPortal launch and returns the launch UUID needed by item and finish calls.
    /// </summary>
    private async Task<string> StartLaunchAsync(IClientService service, ReportPortalLaunchPlan launchPlan,
        string projectName, CancellationToken cancellationToken)
    {
        var launch = await service.Launch.StartAsync(new StartLaunchRequest
        {
            Name = launchPlan.LaunchName,
            Description = launchPlan.Description,
            Mode = launchPlan.DebugMode ? LaunchMode.Debug : LaunchMode.Default,
            StartTime = launchPlan.LaunchStartTimeUtc,
            Attributes = launchPlan.BuildLaunchAttributes()
        }, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Started ReportPortal launch {LaunchUuid} in project {ProjectName} for system {SystemName}.",
            launch.Uuid, projectName, launchPlan.System);

        return launch.Uuid;
    }

    /// <summary>
    /// Publishes queued assertion items and then finishes the launch.
    /// </summary>
    private async Task PublishLaunchItems(IClientService service, ReportPortalLaunchPlan launchPlan,
        string projectName, string launchUuid, CancellationToken cancellationToken)
    {
        PublishQueuedItems(service, launchUuid, launchPlan);

        await FinishLaunchAsync(service, launchUuid, projectName, launchPlan, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Delegates queued assertion publishing back to each passive reporter in the grouped launch.
    /// </summary>
    private void PublishQueuedItems(IClientService service, string launchUuid, ReportPortalLaunchPlan launchPlan)
    {
        var publishContext = new ReportPortalPublishContext(service, launchUuid, launchPlan);
        foreach (var reporterResults in launchPlan.ReporterResults)
        {
            reporterResults.Reporter.PublishQueuedResults(publishContext, reporterResults.Assertions, logger);
        }
    }

    /// <summary>
    /// Finishes a ReportPortal launch while keeping cleanup-time finish failures non-fatal.
    /// </summary>
    private async Task FinishLaunchAsync(IClientService service, string launchUuid, string projectName,
        ReportPortalLaunchPlan launchPlan, CancellationToken cancellationToken)
    {
        try
        {
            var finishedLaunch = await service.Launch.FinishAsync(launchUuid, new FinishLaunchRequest
            {
                EndTime = launchPlan.LaunchEndTimeUtc
            }, cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Finished ReportPortal launch {LaunchUuid} in project {ProjectName} for system {SystemName}.",
                launchUuid, projectName, launchPlan.System);
            logger.LogInformation("ReportPortal report: {ReportUrl}", finishedLaunch.Link);
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Could not finish ReportPortal launch {LaunchUuid} in project {ProjectName}.",
                launchUuid, projectName);
        }
    }

    /// <summary>
    /// Disposes the short-lived ReportPortal client while keeping dispose failures non-fatal.
    /// </summary>
    private void DisposeService(IClientService? service, string projectName, string systemName)
    {
        if (service is not IDisposable disposableService)
            return;

        try
        {
            disposableService.Dispose();
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Could not dispose ReportPortal client service for project {ProjectName} and system {SystemName}.",
                projectName, systemName);
        }
    }

    /// <summary>
    /// Releases the owned validation HTTP client, if this publisher created it.
    /// </summary>
    public void Dispose()
    {
        if (_validationHttpClientDisposed)
            return;

        _validationHttpClientDisposed = true;
        _validationHttpClient.Dispose();
    }
}

internal sealed record ReportPortalPublishContext(
    IClientService Service,
    string LaunchUuid,
    ReportPortalLaunchPlan LaunchPlan);
