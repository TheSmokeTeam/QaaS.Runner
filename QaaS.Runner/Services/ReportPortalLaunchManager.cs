using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QaaS.Runner.Assertions;
using QaaS.Runner.ConfigurationObjects;
using ReportPortal.Client;
using ReportPortal.Client.Abstractions;
using ReportPortal.Client.Abstractions.Models;
using ReportPortal.Client.Abstractions.Requests;
using System.Net.Http;

namespace QaaS.Runner.Services;

/// <summary>
/// Coordinates a shared ReportPortal launch for a single runner invocation.
/// </summary>
public sealed class ReportPortalLaunchManager : IReportPortalLaunchAccessor, IDisposable
{
    private Service? _client;

    public bool IsEnabled { get; private set; }

    public bool IsActive => IsEnabled && !string.IsNullOrWhiteSpace(LaunchUuid) && _client != null;

    public string? LaunchUuid { get; private set; }

    public IClientService Client => _client ?? throw new InvalidOperationException("ReportPortal client is not initialized.");

    public void StartLaunch(ILogger logger, string launchName, string launchDescription,
        IEnumerable<KeyValuePair<string, string?>> attributes)
    {
        if (IsActive)
            return;

        var configuration = LoadConfiguration();
        if (!configuration.Enabled)
            throw new InvalidOperationException(
                "ReportPortal reporting was selected, but ReportPortal.config.json has enabled=false.");

        ValidateConfiguration(configuration);

        _client = new Service(new Uri(configuration.Server.Url), configuration.Server.Project,
            configuration.Server.ApiKey, new DefaultHttpClientFactory());

        var resolvedLaunchName = string.IsNullOrWhiteSpace(launchName) ? configuration.Launch.Name : launchName;
        var resolvedLaunchDescription = string.IsNullOrWhiteSpace(launchDescription)
            ? configuration.Launch.Description
            : launchDescription;
        var launchAttributes = attributes
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key) &&
                                !string.IsNullOrWhiteSpace(attribute.Value))
            .Select(attribute => new ItemAttribute
            {
                Key = attribute.Key,
                Value = attribute.Value
            })
            .ToList();

        var launchResponse = _client.Launch.StartAsync(new StartLaunchRequest
        {
            Name = resolvedLaunchName,
            Description = resolvedLaunchDescription,
            Mode = LaunchMode.Default,
            StartTime = DateTime.UtcNow,
            Attributes = launchAttributes
        }).GetAwaiter().GetResult();

        LaunchUuid = launchResponse.Uuid;
        IsEnabled = true;
        logger.LogInformation("Started ReportPortal launch {LaunchUuid} for project {Project}",
            LaunchUuid, configuration.Server.Project);
    }

    public void FinishLaunch(ILogger logger)
    {
        if (!IsActive || LaunchUuid == null || _client == null)
            return;

        _client.Launch.FinishAsync(LaunchUuid, new FinishLaunchRequest
        {
            EndTime = DateTime.UtcNow
        }).GetAwaiter().GetResult();

        logger.LogInformation("Finished ReportPortal launch {LaunchUuid}", LaunchUuid);
        LaunchUuid = null;
    }

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
        LaunchUuid = null;
    }

    private static ReportPortalConfig LoadConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("ReportPortal.config.json", optional: true)
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "ReportPortal.config.json"), optional: true)
            .AddEnvironmentVariables()
            .Build();

        return configuration.Get<ReportPortalConfig>() ?? new ReportPortalConfig();
    }

    private static void ValidateConfiguration(ReportPortalConfig configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.Server.Url))
            throw new InvalidOperationException("ReportPortal server url must be configured.");

        if (string.IsNullOrWhiteSpace(configuration.Server.Project))
            throw new InvalidOperationException("ReportPortal project must be configured.");

        if (string.IsNullOrWhiteSpace(configuration.Server.ApiKey))
            throw new InvalidOperationException("ReportPortal api key must be configured.");
    }

    private sealed class DefaultHttpClientFactory : IHttpClientFactory
    {
        public HttpClient Create()
        {
            return new HttpClient();
        }
    }
}
