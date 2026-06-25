using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace QaaS.Runner.Assertions.Reporters.ReportPortal;

/// <summary>
/// Validates the minimum ReportPortal prerequisites required for QaaS to publish results. The validator never creates
/// or mutates ReportPortal resources; it only checks endpoint reachability, API key validity, and project visibility.
/// </summary>
/// <remarks>
/// Failures are returned as warning-backed <see cref="ReportPortalAccessResult" /> values. The runner-owned
/// deferred publisher decides whether to hard-fail early validation or skip a final publish attempt.
/// </remarks>
internal sealed class ReportPortalAccessValidator : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly ConcurrentDictionary<string, Lazy<Task<ReportPortalAccessResult>>> _accessCache =
        new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>
    /// Creates a validator that owns its HTTP client.
    /// </summary>
    public ReportPortalAccessValidator() : this(new HttpClient())
    {
        _ownsHttpClient = true;
    }

    /// <summary>
    /// Creates a validator that uses a caller-provided HTTP client.
    /// </summary>
    /// <param name="httpClient">The HTTP client used to query ReportPortal project metadata.</param>
    internal ReportPortalAccessValidator(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Resolves the passive publishing access contract for the given launch plan. Results are cached per endpoint/API
    /// key/project combination so repeated launch groups do not spam the same warning.
    /// </summary>
    /// <param name="launchPlan">The ReportPortal launch plan to validate.</param>
    /// <param name="logger">The logger used for best-effort warning messages.</param>
    /// <param name="cancellationToken">A cancellation token for the outbound validation request.</param>
    /// <returns>The resolved access contract, or a failure result when publishing should be skipped.</returns>
    internal Task<ReportPortalAccessResult> EnsureWriteAccessAsync(ReportPortalLaunchPlan launchPlan, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launchPlan);
        ArgumentNullException.ThrowIfNull(logger);

        var cacheKey = BuildCacheKey(launchPlan);
        var lazyResult = _accessCache.GetOrAdd(cacheKey,
            _ => new Lazy<Task<ReportPortalAccessResult>>(
                () => ValidateCoreAsync(launchPlan, logger, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyResult.Value;
    }

    /// <summary>
    /// Performs the uncached ReportPortal access check for one resolved launch plan.
    /// </summary>
    /// <param name="launchPlan">The ReportPortal launch plan to validate.</param>
    /// <param name="logger">The logger that receives best-effort publishing warnings.</param>
    /// <param name="cancellationToken">A cancellation token for the outbound ReportPortal request.</param>
    /// <returns>
    /// A successful access result when the endpoint, API key, and project are valid; otherwise a failure result with the
    /// warning message that explains why publishing should be skipped.
    /// </returns>
    private async Task<ReportPortalAccessResult> ValidateCoreAsync(ReportPortalLaunchPlan launchPlan, ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(launchPlan.Project))
            return WarnAndReturnFailure(logger,
                "Could not publish results to ReportPortal because ReportPortal.Project or MetaData.Team was not configured.");

        var projectName = launchPlan.Project;

        if (!launchPlan.TryGetEndpointUri(out var endpointUri, out var endpointFailureReason))
            return WarnAndReturnFailure(logger, 
                $"Could not publish results to ReportPortal: {endpointFailureReason}");

        if (string.IsNullOrWhiteSpace(launchPlan.ApiKey))
            return WarnAndReturnFailure(logger,
                $"Could not publish results to ReportPortal project `{projectName}` because ReportPortal.ApiKey was not configured.");
        

        var apiKey = launchPlan.ApiKey;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri(endpointUri!, $"v1/project/{Uri.EscapeDataString(projectName)}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return WarnAndReturnFailure(logger,
                    $"Could not publish results to ReportPortal because the configured API key was rejected for project `{projectName}`.");
            
            if (!response.IsSuccessStatusCode)
                return WarnAndReturnFailure(logger, 
                    response.StatusCode == HttpStatusCode.NotFound ? $"Could not publish results to ReportPortal because no accessible project matches `{projectName}`." : $"Could not publish results to ReportPortal at {endpointUri} for project `{projectName}`. Status={(int)response.StatusCode} {response.ReasonPhrase}. Response={responseBody}");
            
            var project = JsonSerializer.Deserialize<ProjectResponse>(responseBody, _jsonSerializerOptions)
                          ?? new ProjectResponse();
            
            if (string.IsNullOrWhiteSpace(project.ProjectName))
                return WarnAndReturnFailure(logger,
                    $"Could not publish results to ReportPortal because the endpoint `{launchPlan.Endpoint}` returned an unreadable project payload for project `{projectName}`.");
            
            return ReportPortalAccessResult.Success(endpointUri!, project.ProjectName, apiKey);
        }
        catch (TaskCanceledException)
        {
            return WarnAndReturnFailure(logger,
                $"Could not publish results to ReportPortal because the endpoint `{launchPlan.Endpoint}` timed out.");
        }
        catch (HttpRequestException exception)
        {
            return WarnAndReturnFailure(logger,
                $"Could not publish results to ReportPortal because the endpoint `{launchPlan.Endpoint}` is unreachable. {exception.Message}");
        }
        catch (JsonException exception)
        {
            return WarnAndReturnFailure(logger,
                $"Could not publish results to ReportPortal because the endpoint `{launchPlan.Endpoint}` returned an unreadable project payload. {exception.Message}");
        }
    }

    private static string BuildCacheKey(ReportPortalLaunchPlan launchPlan)
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

    private static ReportPortalAccessResult WarnAndReturnFailure(ILogger logger, string warningMessage)
    {
        logger.LogWarning(warningMessage);
        return ReportPortalAccessResult.Failure(warningMessage);
    }

    /// <summary>
    /// Releases the owned HTTP client, when this validator created it.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_ownsHttpClient)
            _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class ProjectResponse
    {
        public string? ProjectName { get; init; }
    }
}

/// <summary>
/// Captures the resolved write access contract for one ReportPortal project.
/// </summary>
/// <remarks>
/// A successful result contains the normalized endpoint URI, the project name returned by ReportPortal, and the API key
/// that should be used by the final publisher.
/// </remarks>
internal sealed class ReportPortalAccessResult
{
    private ReportPortalAccessResult(bool canPublish, Uri? endpointUri, string? project, string? apiKey,
        string? failureReason)
    {
        CanPublish = canPublish;
        EndpointUri = endpointUri;
        Project = project;
        ApiKey = apiKey;
        FailureReason = failureReason;
    }

    /// <summary>
    /// Gets whether publishing may proceed.
    /// </summary>
    public bool CanPublish { get; }

    /// <summary>
    /// Gets the normalized ReportPortal API endpoint when publishing is allowed.
    /// </summary>
    public Uri? EndpointUri { get; }

    /// <summary>
    /// Gets the ReportPortal project name returned by the server.
    /// </summary>
    public string? Project { get; }

    /// <summary>
    /// Gets the API key that should be used for publishing.
    /// </summary>
    public string? ApiKey { get; }

    /// <summary>
    /// Gets the reason publishing was disabled or denied.
    /// </summary>
    public string? FailureReason { get; }

    /// <summary>
    /// Creates a successful access result.
    /// </summary>
    /// <param name="endpointUri">The normalized ReportPortal API endpoint.</param>
    /// <param name="project">The ReportPortal project returned by the server.</param>
    /// <param name="apiKey">The API key used for publishing.</param>
    /// <returns>A result that allows publishing to continue.</returns>
    public static ReportPortalAccessResult Success(Uri endpointUri, string project, string apiKey)
    {
        return new ReportPortalAccessResult(true, endpointUri, project, apiKey, null);
    }

    /// <summary>
    /// Creates a failed access result.
    /// </summary>
    /// <param name="failureReason">The reason publishing should be skipped.</param>
    /// <returns>A result that prevents publishing for the current launch group.</returns>
    public static ReportPortalAccessResult Failure(string failureReason)
    {
        return new ReportPortalAccessResult(false, null, null, null, failureReason);
    }
}
