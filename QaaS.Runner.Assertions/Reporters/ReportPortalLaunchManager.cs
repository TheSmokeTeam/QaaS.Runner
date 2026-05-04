using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Runner.Infrastructure;
using ReportPortal.Client;
using ReportPortal.Client.Abstractions.Models;
using ReportPortal.Client.Abstractions.Requests;
using ReportPortal.Client.Abstractions.Responses;

namespace QaaS.Runner.Assertions.Reporters;

public interface IReportPortalLaunchAccessor
{
    bool IsStarted { get; }

    string LaunchUuid { get; }

    IReportPortalClient Client { get; }
}

public interface IReportPortalLaunchManager : IReportPortalLaunchAccessor
{
    void Start(Context context, DateTime testSuiteStartTimeUtc);

    void Finish();
}

internal sealed class ReportPortalLaunchManager : IReportPortalLaunchManager
{
    private const string DefaultConfigFileName = "ReportPortal.config.json";
    private const string QaaSTag = "QaaS";
    private readonly IReportPortalClientFactory _clientFactory;
    private readonly IFileSystem _fileSystem;
    private readonly string? _configPath;
    private IReportPortalClient? _client;
    private string? _launchUuid;
    private ReportPortalReporterConfig? _config;

    public ReportPortalLaunchManager()
        : this(new ReportPortalClientFactory(), new FileSystem())
    {
    }

    internal ReportPortalLaunchManager(
        IReportPortalClientFactory clientFactory,
        IFileSystem? fileSystem = null,
        string? configPath = null)
    {
        _clientFactory = clientFactory;
        _fileSystem = fileSystem ?? new FileSystem();
        _configPath = configPath;
    }

    public bool IsStarted => _client != null && !string.IsNullOrWhiteSpace(_launchUuid);

    public string LaunchUuid => _launchUuid ??
                                throw new InvalidOperationException("ReportPortal launch has not been started.");

    public IReportPortalClient Client => _client ??
                                         throw new InvalidOperationException("ReportPortal launch has not been started.");

    public void Start(Context context, DateTime testSuiteStartTimeUtc)
    {
        if (IsStarted)
            return;

        var config = GetConfig();
        if (!config.Enabled)
            return;

        var metadata = ResolveMetadata(context);
        var project = ResolveProjectName(metadata);
        _client = _clientFactory.Create(config.Server.Url, project, config.Server.ApiKey);
        var launch = _client.StartLaunchAsync(new StartLaunchRequest
        {
            Name = ResolveLaunchName(metadata),
            Description = "QaaS Runner execution",
            Mode = config.Launch.DebugMode ? LaunchMode.Debug : LaunchMode.Default,
            StartTime = testSuiteStartTimeUtc,
            Attributes = BuildLaunchAttributes(context, metadata)
        }).GetAwaiter().GetResult();

        _launchUuid = launch.Uuid;
    }

    public void Finish()
    {
        try
        {
            if (!IsStarted)
                return;

            _client!.FinishLaunchAsync(_launchUuid!, new FinishLaunchRequest
            {
                EndTime = DateTime.UtcNow
            }).GetAwaiter().GetResult();
        }
        finally
        {
            _client?.Dispose();
            _client = null;
            _launchUuid = null;
        }
    }

    private ReportPortalReporterConfig GetConfig()
    {
        if (_config != null)
            return _config;

        var path = ResolveConfigPath();
        var json = _fileSystem.File.ReadAllText(path);
        _config = JsonSerializer.Deserialize<ReportPortalReporterConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new ReportPortalReporterConfig();

        if (string.IsNullOrWhiteSpace(_config.Server.Url))
            throw new InvalidOperationException("ReportPortal server URL must be configured in ReportPortal.config.json.");
        if (string.IsNullOrWhiteSpace(_config.Server.ApiKey))
            throw new InvalidOperationException("ReportPortal API key must be configured in ReportPortal.config.json.");

        return _config;
    }

    private string ResolveConfigPath()
    {
        var candidatePaths = new[]
            {
                _configPath,
                Path.Combine(AppContext.BaseDirectory, DefaultConfigFileName),
                Path.Combine(Environment.CurrentDirectory, DefaultConfigFileName)
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var candidatePath in candidatePaths)
        {
            if (_fileSystem.File.Exists(candidatePath))
                return candidatePath;
        }

        throw new InvalidOperationException($"Could not find {DefaultConfigFileName}.");
    }

    private static QaaS.Framework.SDK.MetaDataConfig ResolveMetadata(Context context)
    {
        if (context is not InternalContext internalContext)
            throw new InvalidOperationException("ReportPortal project must be resolved from runner metadata.");

        return internalContext.GetValueFromGlobalDictionary(internalContext.GetMetaDataPath()) as QaaS.Framework.SDK.MetaDataConfig ??
               internalContext.GetMetaDataOrDefault();
    }

    private static string ResolveProjectName(QaaS.Framework.SDK.MetaDataConfig metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.Team))
            throw new InvalidOperationException("ReportPortal project must be configured in metadata Team.");

        return metadata.Team;
    }

    private static string ResolveLaunchName(QaaS.Framework.SDK.MetaDataConfig metadata)
    {
        var system = string.IsNullOrWhiteSpace(metadata.System) ? "UnknownSystem" : metadata.System;
        return $"{metadata.Team} {system}";
    }

    private static List<ItemAttribute> BuildLaunchAttributes(Context context, QaaS.Framework.SDK.MetaDataConfig metadata)
    {
        var attributes = new List<ItemAttribute>
        {
            Attribute("tag", QaaSTag),
            Attribute("team", metadata.Team!)
        };

        if (!string.IsNullOrWhiteSpace(metadata.System))
            attributes.Add(Attribute("system", metadata.System));
        if (!string.IsNullOrWhiteSpace(context.ExecutionId))
            attributes.Add(Attribute("executionId", context.ExecutionId));
        if (!string.IsNullOrWhiteSpace(context.CaseName))
            attributes.Add(Attribute("caseName", context.CaseName));

        return attributes;
    }

    private static ItemAttribute Attribute(string key, string value)
    {
        return new ItemAttribute
        {
            Key = key,
            Value = value
        };
    }
}

internal sealed class ReportPortalReporterConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("server")]
    public ReportPortalServerConfig Server { get; init; } = new();

    [JsonPropertyName("launch")]
    public ReportPortalLaunchConfig Launch { get; init; } = new();
}

internal sealed class ReportPortalServerConfig
{
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; init; } = string.Empty;
}

internal sealed class ReportPortalLaunchConfig
{
    [JsonPropertyName("debugMode")]
    public bool DebugMode { get; init; }
}

public interface IReportPortalClientFactory
{
    IReportPortalClient Create(string serverUrl, string projectName, string apiKey);
}

public interface IReportPortalClient : IDisposable
{
    Task<LaunchCreatedResponse> StartLaunchAsync(StartLaunchRequest request);

    Task<LaunchFinishedResponse> FinishLaunchAsync(string launchUuid, FinishLaunchRequest request);

    Task<TestItemCreatedResponse> StartTestItemAsync(StartTestItemRequest request);

    Task<TestItemCreatedResponse> StartChildTestItemAsync(string parentItemUuid, StartTestItemRequest request);

    Task<MessageResponse> FinishTestItemAsync(string itemUuid, FinishTestItemRequest request);

    Task<LogItemCreatedResponse> CreateLogItemAsync(CreateLogItemRequest request);
}

internal sealed class ReportPortalClientFactory : IReportPortalClientFactory
{
    public IReportPortalClient Create(string serverUrl, string projectName, string apiKey)
    {
        return new ReportPortalClientAdapter(new Service(new Uri(serverUrl), projectName, apiKey));
    }
}

internal sealed class ReportPortalClientAdapter(Service service) : IReportPortalClient
{
    public Task<LaunchCreatedResponse> StartLaunchAsync(StartLaunchRequest request) =>
        service.Launch.StartAsync(request);

    public Task<LaunchFinishedResponse> FinishLaunchAsync(string launchUuid, FinishLaunchRequest request) =>
        service.Launch.FinishAsync(launchUuid, request);

    public Task<TestItemCreatedResponse> StartTestItemAsync(StartTestItemRequest request) =>
        service.TestItem.StartAsync(request);

    public Task<TestItemCreatedResponse> StartChildTestItemAsync(string parentItemUuid, StartTestItemRequest request) =>
        service.TestItem.StartAsync(parentItemUuid, request);

    public Task<MessageResponse> FinishTestItemAsync(string itemUuid, FinishTestItemRequest request) =>
        service.TestItem.FinishAsync(itemUuid, request);

    public Task<LogItemCreatedResponse> CreateLogItemAsync(CreateLogItemRequest request) =>
        service.LogItem.CreateAsync(request);

    public void Dispose()
    {
        service.Dispose();
    }
}
