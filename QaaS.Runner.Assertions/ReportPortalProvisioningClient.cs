using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using QaaS.Runner.Assertions.ConfigurationObjects;

namespace QaaS.Runner.Assertions;

/// <summary>
/// Provisions the ReportPortal project prerequisites that QaaS needs before it can open a launch:
/// the target project, the managed project bot user, the API key used for reporting, and the default saved filters.
/// </summary>
internal sealed class ReportPortalProvisioningClient : IDisposable
{
    private const string ManagedApiKeyName = "qaas-runner";
    private const string InternalEntryType = "INTERNAL";
    private const string UserAccountRole = "USER";
    private const string ProjectManagerRole = "PROJECT_MANAGER";
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private bool _disposed;

    public ReportPortalProvisioningClient() : this(new HttpClient())
    {
        _ownsHttpClient = true;
    }

    internal ReportPortalProvisioningClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Ensures that the ReportPortal project contract required by the current run exists and returns
    /// the API key that should be used by the reporting client for launch and test-item publishing.
    /// </summary>
    internal async Task<ReportPortalProvisioningResult> EnsureProjectAccessAsync(ReportPortalSettings settings,
        ILogger logger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        if (!settings.Enabled)
            return ReportPortalProvisioningResult.Disabled;

        if (!settings.UsesManagedProjectBot)
        {
            logger.LogInformation(
                "ReportPortal project {ProjectName} will use the explicitly supplied API key override without managed provisioning.",
                settings.Project);
            return new ReportPortalProvisioningResult(settings.Project, settings.ApiKey!);
        }

        var bootstrapToken = await AuthenticateAsync(settings, settings.BootstrapUsername, settings.BootstrapPassword,
            cancellationToken).ConfigureAwait(false);

        await EnsureProjectExistsAsync(settings, bootstrapToken, logger, cancellationToken).ConfigureAwait(false);

        var managedUser = await EnsureManagedUserAsync(settings, bootstrapToken, logger, cancellationToken)
            .ConfigureAwait(false);
        var filterOwnerToken = await AuthenticateAsync(settings, settings.ManagedBotLogin,
                settings.BuildManagedBotPassword(), cancellationToken)
            .ConfigureAwait(false);
        var apiKey = await EnsureApiKeyAsync(settings, managedUser.Id, filterOwnerToken, logger, cancellationToken)
            .ConfigureAwait(false);

        await EnsureFiltersAsync(settings, filterOwnerToken, logger, cancellationToken).ConfigureAwait(false);

        return new ReportPortalProvisioningResult(settings.Project, apiKey);
    }

    private async Task<string> AuthenticateAsync(ReportPortalSettings settings, string username, string password,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.GatewayUri, "uat/sso/oauth/token"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = username,
                ["password"] = password
            })
        };

        var basicValue = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{settings.BootstrapClientId}:{settings.BootstrapClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicValue);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw BuildHttpFailure(
                $"ReportPortal authentication failed for user `{username}` against {request.RequestUri}.", response,
                body);

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(body, _jsonSerializerOptions)
                            ?? throw new InvalidOperationException(
                                "ReportPortal returned an empty authentication response.");
        return !string.IsNullOrWhiteSpace(tokenResponse.AccessToken)
            ? tokenResponse.AccessToken
            : throw new InvalidOperationException(
                "ReportPortal authentication response did not contain an access token.");
    }

    private async Task EnsureProjectExistsAsync(ReportPortalSettings settings, string bootstrapToken, ILogger logger,
        CancellationToken cancellationToken)
    {
        var projectNames = await SendAsync<string[]>(settings, HttpMethod.Get,
            $"api/v1/project/names/search?term={Uri.EscapeDataString(settings.Project)}",
            bootstrapToken, null, cancellationToken).ConfigureAwait(false) ?? [];

        if (projectNames.Any(projectName => string.Equals(projectName, settings.Project, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogDebug("ReportPortal project {ProjectName} already exists.", settings.Project);
            return;
        }

        await SendAsync<EntryCreatedResponse>(settings, HttpMethod.Post, "api/v1/project", bootstrapToken,
            new CreateProjectRequest
            {
                ProjectName = settings.Project,
                EntryType = InternalEntryType
            }, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Created ReportPortal project {ProjectName}.", settings.Project);
    }

    private async Task<ReportPortalManagedUser> EnsureManagedUserAsync(ReportPortalSettings settings,
        string bootstrapToken, ILogger logger, CancellationToken cancellationToken)
    {
        var existingUser = await GetUserAsync(settings, settings.ManagedBotLogin, bootstrapToken, cancellationToken)
            .ConfigureAwait(false);

        if (existingUser is null)
        {
            var createdUser = await SendAsync<CreateUserResponse>(settings, HttpMethod.Post, "api/users", bootstrapToken,
                new CreateUserRequest
                {
                    Active = true,
                    AccountType = InternalEntryType,
                    Login = settings.ManagedBotLogin,
                    Password = settings.BuildManagedBotPassword(),
                    FullName = settings.ManagedBotFullName,
                    Email = settings.ManagedBotEmail,
                    AccountRole = UserAccountRole,
                    ProjectRole = ProjectManagerRole,
                    DefaultProject = settings.Project,
                    ExternalId = settings.ManagedBotExternalId
                }, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"ReportPortal did not return the created user payload for managed bot `{settings.ManagedBotLogin}`.");

            logger.LogInformation(
                "Created ReportPortal managed bot user {Login} for project {ProjectName}.",
                settings.ManagedBotLogin, settings.Project);
            return new ReportPortalManagedUser(createdUser.Id, settings.ManagedBotLogin);
        }

        if (!string.Equals(existingUser.ExternalId, settings.ManagedBotExternalId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"ReportPortal user `{settings.ManagedBotLogin}` already exists but is not managed by QaaS. " +
                "Remove or rename that user before enabling team-scoped ReportPortal provisioning.");
        }

        var userProjects = await SendAsync<Dictionary<string, AssignedProjectResource>>(settings, HttpMethod.Get,
            $"api/users/{Uri.EscapeDataString(settings.ManagedBotLogin)}/projects", bootstrapToken, null,
            cancellationToken).ConfigureAwait(false) ?? new Dictionary<string, AssignedProjectResource>();
        var assignedProjectPair = userProjects.FirstOrDefault(projectPair =>
            string.Equals(projectPair.Key, settings.Project, StringComparison.OrdinalIgnoreCase));
        if (assignedProjectPair.Equals(default(KeyValuePair<string, AssignedProjectResource>)) ||
            assignedProjectPair.Value is null ||
            string.IsNullOrWhiteSpace(assignedProjectPair.Key) ||
            !string.Equals(assignedProjectPair.Value.ProjectRole, ProjectManagerRole, StringComparison.OrdinalIgnoreCase))
        {
            await SendAsync<object>(settings, HttpMethod.Put,
                $"api/v1/project/{Uri.EscapeDataString(settings.Project)}/assign", bootstrapToken,
                new AssignUsersRequest
                {
                    UserNames = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [settings.ManagedBotLogin] = ProjectManagerRole
                    }
                }, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Assigned ReportPortal managed bot user {Login} to project {ProjectName} as {Role}.",
                settings.ManagedBotLogin, settings.Project, ProjectManagerRole);
        }

        return new ReportPortalManagedUser(existingUser.Id, settings.ManagedBotLogin);
    }

    private async Task<string> EnsureApiKeyAsync(ReportPortalSettings settings, long userId, string botToken,
        ILogger logger, CancellationToken cancellationToken)
    {
        var keys = await SendAsync<ApiKeysResponse>(settings, HttpMethod.Get,
            $"api/users/{userId}/api-keys", botToken, null, cancellationToken).ConfigureAwait(false);
        var existingKey = keys?.Items?.FirstOrDefault(apiKey =>
            string.Equals(apiKey.Name, ManagedApiKeyName, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(existingKey?.ApiKey))
        {
            logger.LogDebug("Reusing existing ReportPortal API key {ApiKeyName} for project {ProjectName}.",
                ManagedApiKeyName, settings.Project);
            return existingKey.ApiKey!;
        }

        if (existingKey is not null)
        {
            await SendAsync<object>(settings, HttpMethod.Delete,
                $"api/users/{userId}/api-keys/{existingKey.Id}", botToken, null, cancellationToken)
                .ConfigureAwait(false);
            logger.LogInformation(
                "Rotated existing ReportPortal API key {ApiKeyName} for managed bot {Login} because the key value could not be reused.",
                ManagedApiKeyName, settings.ManagedBotLogin);
        }

        var createdKey = await SendAsync<ApiKeyResponse>(settings, HttpMethod.Post,
            $"api/users/{userId}/api-keys", botToken,
            new ApiKeyCreateRequest
            {
                Name = ManagedApiKeyName
            }, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"ReportPortal did not return the created API key payload for user `{settings.ManagedBotLogin}`.");

        if (string.IsNullOrWhiteSpace(createdKey.ApiKey))
        {
            throw new InvalidOperationException(
                $"ReportPortal created API key `{ManagedApiKeyName}` for `{settings.ManagedBotLogin}` but did not return its value.");
        }

        logger.LogInformation("Created ReportPortal API key {ApiKeyName} for managed bot {Login}.",
            ManagedApiKeyName, settings.ManagedBotLogin);
        return createdKey.ApiKey;
    }

    private async Task EnsureFiltersAsync(ReportPortalSettings settings, string filterOwnerToken, ILogger logger,
        CancellationToken cancellationToken)
    {
        var existingFilters = await SendAsync<PageUserFilterResponse>(settings, HttpMethod.Get,
            $"api/v1/{Uri.EscapeDataString(settings.Project)}/filter?page.page=0&page.size=200",
            filterOwnerToken, null, cancellationToken).ConfigureAwait(false);
        var existingByName = (existingFilters?.Content ?? [])
            .ToDictionary(filter => filter.Name, filter => filter, StringComparer.Ordinal);

        foreach (var filterSpec in BuildDefaultFilters(settings))
        {
            if (existingByName.TryGetValue(filterSpec.Name, out var existingFilter))
            {
                logger.LogDebug(
                    "ReportPortal filter {FilterName} already exists in project {ProjectName}; skipping recreation.",
                    existingFilter.Name, settings.Project);
            }
            else
            {
                await SendAsync<EntryCreatedResponse>(settings, HttpMethod.Post,
                    $"api/v1/{Uri.EscapeDataString(settings.Project)}/filter", filterOwnerToken,
                    new CreateFilterRequest
                    {
                        Name = filterSpec.Name,
                        Description = filterSpec.Description,
                        Type = filterSpec.Type,
                        Conditions = filterSpec.Conditions,
                        Orders = filterSpec.Orders
                    }, cancellationToken).ConfigureAwait(false);

                logger.LogInformation("Created ReportPortal filter {FilterName} in project {ProjectName}.",
                    filterSpec.Name, settings.Project);
            }
        }
    }

    private static IReadOnlyList<ReportPortalFilterSpecification> BuildDefaultFilters(ReportPortalSettings settings)
    {
        return
        [
            new ReportPortalFilterSpecification(
                "QaaS Recent Runs",
                "launch",
                "Recent QaaS runner launches in this ReportPortal project.",
                [
                    new UserFilterConditionRequest
                    {
                        FilteringField = "compositeAttribute",
                        Condition = "has",
                        Value = "tool:QaaS"
                    }
                ],
                [BuildStartTimeDescendingOrder()]),
            new ReportPortalFilterSpecification(
                "QaaS Failed Runs",
                "launch",
                "QaaS runner launches with at least one failed execution.",
                [
                    new UserFilterConditionRequest
                    {
                        FilteringField = "compositeAttribute",
                        Condition = "has",
                        Value = "tool:QaaS"
                    },
                    new UserFilterConditionRequest
                    {
                        FilteringField = "statistics$executions$failed",
                        Condition = "gte",
                        Value = "1"
                    }
                ],
                [BuildStartTimeDescendingOrder()]),
            new ReportPortalFilterSpecification(
                "QaaS Failed Assertions",
                "testitem",
                "QaaS assertion items that finished in a failed or interrupted state.",
                [
                    new UserFilterConditionRequest
                    {
                        FilteringField = "compositeAttribute",
                        Condition = "has",
                        Value = "tool:QaaS"
                    },
                    new UserFilterConditionRequest
                    {
                        FilteringField = "status",
                        Condition = "in",
                        Value = "FAILED,INTERRUPTED"
                    }
                ],
                [BuildStartTimeDescendingOrder()]),
            new ReportPortalFilterSpecification(
                $"QaaS {settings.System} Runs",
                "launch",
                $"QaaS runner launches for system {settings.System}.",
                [
                    new UserFilterConditionRequest
                    {
                        FilteringField = "compositeAttribute",
                        Condition = "has",
                        Value = "tool:QaaS"
                    },
                    new UserFilterConditionRequest
                    {
                        FilteringField = "compositeAttribute",
                        Condition = "has",
                        Value = $"system:{settings.System}"
                    }
                ],
                [BuildStartTimeDescendingOrder()])
        ];
    }

    private static OrderRequest BuildStartTimeDescendingOrder()
    {
        return new OrderRequest
        {
            SortingColumn = "startTime",
            IsAsc = false
        };
    }

    private async Task<UserResponse?> GetUserAsync(ReportPortalSettings settings, string login, string token,
        CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(settings, HttpMethod.Get,
            $"api/users/{Uri.EscapeDataString(login)}", token, null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw BuildHttpFailure($"Failed to load ReportPortal user `{login}`.", response, body);

        return JsonSerializer.Deserialize<UserResponse>(body, _jsonSerializerOptions);
    }

    private async Task<T?> SendAsync<T>(ReportPortalSettings settings, HttpMethod method, string relativePath,
        string bearerToken, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(settings, method, relativePath, bearerToken, body, cancellationToken)
            .ConfigureAwait(false);
        if (typeof(T) == typeof(object))
        {
            if (!response.IsSuccessStatusCode)
            {
                var failedBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw BuildHttpFailure($"ReportPortal request `{method} {relativePath}` failed.", response, failedBody);
            }

            return default;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw BuildHttpFailure($"ReportPortal request `{method} {relativePath}` failed.", response, responseBody);

        if (string.IsNullOrWhiteSpace(responseBody))
            return default;

        return JsonSerializer.Deserialize<T>(responseBody, _jsonSerializerOptions);
    }

    private async Task<HttpResponseMessage> SendRawAsync(ReportPortalSettings settings, HttpMethod method,
        string relativePath, string bearerToken, object? body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, new Uri(settings.GatewayUri, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, _jsonSerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static InvalidOperationException BuildHttpFailure(string message, HttpResponseMessage response, string body)
    {
        return new InvalidOperationException(
            $"{message} Status={(int)response.StatusCode} {response.ReasonPhrase}. Response={body}");
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

    internal sealed record ReportPortalProvisioningResult(string Project, string ApiKey)
    {
        public static ReportPortalProvisioningResult Disabled { get; } = new(string.Empty, string.Empty);
    }

    private sealed record ReportPortalManagedUser(long Id, string Login);

    private sealed record ReportPortalFilterSpecification(
        string Name,
        string Type,
        string Description,
        List<UserFilterConditionRequest> Conditions,
        List<OrderRequest> Orders);

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }
    }

    private sealed class EntryCreatedResponse
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }
    }

    private sealed class CreateProjectRequest
    {
        [JsonPropertyName("projectName")]
        public string ProjectName { get; init; } = string.Empty;

        [JsonPropertyName("entryType")]
        public string EntryType { get; init; } = string.Empty;
    }

    private sealed class CreateUserRequest
    {
        [JsonPropertyName("active")]
        public bool Active { get; init; }

        [JsonPropertyName("externalId")]
        public string ExternalId { get; init; } = string.Empty;

        [JsonPropertyName("accountType")]
        public string AccountType { get; init; } = string.Empty;

        [JsonPropertyName("login")]
        public string Login { get; init; } = string.Empty;

        [JsonPropertyName("password")]
        public string Password { get; init; } = string.Empty;

        [JsonPropertyName("fullName")]
        public string FullName { get; init; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; init; } = string.Empty;

        [JsonPropertyName("accountRole")]
        public string AccountRole { get; init; } = string.Empty;

        [JsonPropertyName("projectRole")]
        public string ProjectRole { get; init; } = string.Empty;

        [JsonPropertyName("defaultProject")]
        public string DefaultProject { get; init; } = string.Empty;
    }

    private sealed class CreateUserResponse
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }
    }

    private sealed class UserResponse
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("userId")]
        public string? UserId { get; init; }

        [JsonPropertyName("externalId")]
        public string? ExternalId { get; init; }
    }

    private sealed class AssignUsersRequest
    {
        [JsonPropertyName("userNames")]
        public Dictionary<string, string> UserNames { get; init; } = new(StringComparer.Ordinal);
    }

    private sealed class AssignedProjectResource
    {
        [JsonPropertyName("projectRole")]
        public string? ProjectRole { get; init; }
    }

    private sealed class ApiKeysResponse
    {
        [JsonPropertyName("items")]
        public List<ApiKeyResponse>? Items { get; init; }
    }

    private sealed class ApiKeyResponse
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("api_key")]
        public string? ApiKey { get; init; }
    }

    private sealed class ApiKeyCreateRequest
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
    }

    private sealed class PageUserFilterResponse
    {
        [JsonPropertyName("content")]
        public List<UserFilterResponse>? Content { get; init; }
    }

    private sealed class UserFilterResponse
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
    }

    private sealed class CreateFilterRequest
    {
        [JsonPropertyName("description")]
        public string Description { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("conditions")]
        public List<UserFilterConditionRequest> Conditions { get; init; } = [];

        [JsonPropertyName("orders")]
        public List<OrderRequest> Orders { get; init; } = [];
    }

    private sealed class UserFilterConditionRequest
    {
        [JsonPropertyName("filteringField")]
        public string FilteringField { get; init; } = string.Empty;

        [JsonPropertyName("condition")]
        public string Condition { get; init; } = string.Empty;

        [JsonPropertyName("value")]
        public string Value { get; init; } = string.Empty;
    }

    private sealed class OrderRequest
    {
        [JsonPropertyName("sortingColumn")]
        public string SortingColumn { get; init; } = string.Empty;

        [JsonPropertyName("isAsc")]
        public bool IsAsc { get; init; }
    }
}
