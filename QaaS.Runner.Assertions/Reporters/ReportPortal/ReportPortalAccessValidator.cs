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

    public ReportPortalAccessValidator() : this(new HttpClient())
    {
        _ownsHttpClient = true;
    }

    internal ReportPortalAccessValidator(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Resolves the passive publishing access contract for the given settings. Results are cached per endpoint/API
    /// key/team combination so repeated assertions do not spam the same warning.
    /// </summary>
    internal Task<ReportPortalAccessResult> EnsureWriteAccessAsync(ReportPortalSettings settings, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        if (!settings.Enabled)
            return Task.FromResult(ReportPortalAccessResult.Disabled);

        var cacheKey = string.Join("::",
            settings.Endpoint ?? "<missing-endpoint>",
            settings.ApiKey ?? "<missing-api-key>",
            settings.Team ?? "<missing-project>");

        var lazyResult = _accessCache.GetOrAdd(cacheKey,
            _ => new Lazy<Task<ReportPortalAccessResult>>(
                () => ValidateCoreAsync(settings, logger, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyResult.Value;
    }

    private async Task<ReportPortalAccessResult> ValidateCoreAsync(ReportPortalSettings settings, ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Team))
            return WarnAndReturnFailure(logger,
                "Could not publish results to ReportPortal because MetaData.Team was not configured.");

        var team = settings.Team;

        if (!settings.TryGetEndpointUri(out var endpointUri, out var endpointFailureReason))
            return WarnAndReturnFailure(logger, $"Could not publish results to ReportPortal: {endpointFailureReason}");

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return WarnAndReturnFailure(logger,
                $"Could not publish results to ReportPortal project `{team}` because ReportPortal.ApiKey was not configured.");
        }

        var apiKey = settings.ApiKey;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri(endpointUri!, $"v1/project/{Uri.EscapeDataString(team)}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return WarnAndReturnFailure(logger,
                    $"Could not publish results to ReportPortal because the configured API key was rejected for team `{team}`.");
            }

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return WarnAndReturnFailure(logger,
                        $"Could not publish results to ReportPortal because no accessible project matches team `{team}`.");
                }

                return WarnAndReturnFailure(logger,
                    $"Could not publish results to ReportPortal at {endpointUri} for team `{team}`. Status={(int)response.StatusCode} {response.ReasonPhrase}. Response={responseBody}");
            }

            var project = JsonSerializer.Deserialize<ProjectResponse>(responseBody, _jsonSerializerOptions)
                          ?? new ProjectResponse();
            if (string.IsNullOrWhiteSpace(project.ProjectName))
            {
                return WarnAndReturnFailure(logger,
                    $"Could not publish results to ReportPortal because the endpoint `{settings.Endpoint}` returned an unreadable project payload for team `{team}`.");
            }

            return ReportPortalAccessResult.Success(endpointUri!, project.ProjectName, apiKey);
        }
        catch (TaskCanceledException)
        {
            return WarnAndReturnFailure(logger,
                $"Could not publish results to ReportPortal because the endpoint `{settings.Endpoint}` timed out.");
        }
        catch (HttpRequestException exception)
        {
            return WarnAndReturnFailure(logger,
                $"Could not publish results to ReportPortal because the endpoint `{settings.Endpoint}` is unreachable. {exception.Message}");
        }
        catch (JsonException exception)
        {
            return WarnAndReturnFailure(logger,
                $"Could not publish results to ReportPortal because the endpoint `{settings.Endpoint}` returned an unreadable project payload. {exception.Message}");
        }
    }

    private static ReportPortalAccessResult WarnAndReturnFailure(ILogger logger, string warningMessage)
    {
        logger.LogWarning(warningMessage);
        return ReportPortalAccessResult.Failure(warningMessage);
    }

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

    public bool CanPublish { get; }
    public Uri? EndpointUri { get; }
    public string? Project { get; }
    public string? ApiKey { get; }
    public string? FailureReason { get; }

    public static ReportPortalAccessResult Disabled { get; } = new(false, null, null, null, null);

    public static ReportPortalAccessResult Success(Uri endpointUri, string project, string apiKey)
    {
        return new ReportPortalAccessResult(true, endpointUri, project, apiKey, null);
    }

    public static ReportPortalAccessResult Failure(string failureReason)
    {
        return new ReportPortalAccessResult(false, null, null, null, failureReason);
    }
}
