using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Runner.Infrastructure;
using ReportPortal.Client;
using ReportPortal.Client.Abstractions.Models;
using ReportPortal.Client.Abstractions.Requests;
using ReportPortal.Client.Abstractions.Responses;
using RpLogLevel = ReportPortal.Client.Abstractions.Models.LogLevel;
using RpStatus = ReportPortal.Client.Abstractions.Models.Status;
using AssertionStatus = QaaS.Framework.SDK.Hooks.Assertion.AssertionStatus;

namespace QaaS.Runner.Assertions.Reporters;

/// <inheritdoc />
public class ReportPortalReporter : BaseReporter
{
    private const string DefaultConfigFileName = "ReportPortal.config.json";
    private readonly IReportPortalClientFactory _clientFactory;
    private readonly string? _configPath;
    private IReportPortalClient? _client;
    private ReportPortalReporterConfig? _config;
    private string? _launchUuid;
    private bool _launchStarted;

    public ReportPortalReporter()
        : this(new ReportPortalClientFactory())
    {
    }

    internal ReportPortalReporter(IReportPortalClientFactory clientFactory, string? configPath = null)
    {
        _clientFactory = clientFactory;
        _configPath = configPath;
    }

    protected override void WriteReportCase(ReportCase reportCase)
    {
        if (!GetConfig().Enabled)
        {
            Context.Logger.LogDebug("ReportPortal reporting is disabled by configuration.");
            return;
        }

        var launchUuid = EnsureLaunchStarted();
        var testItem = _client!.StartTestItemAsync(new StartTestItemRequest
        {
            LaunchUuid = launchUuid,
            Name = reportCase.Name,
            Description = reportCase.Description,
            StartTime = reportCase.Start,
            Type = TestItemType.Test,
            UniqueId = reportCase.UniqueId,
            TestCaseId = reportCase.UniqueId,
            CodeReference = reportCase.FullName,
            HasStats = true,
            Parameters = ToParameters(reportCase.Parameters),
            Attributes = BuildCaseAttributes(reportCase)
        }).GetAwaiter().GetResult();

        WriteReportCaseLogs(testItem.Uuid, reportCase);
        WriteAttachments(testItem.Uuid, reportCase.Attachments);
        foreach (var step in reportCase.Steps)
            WriteStep(testItem.Uuid, launchUuid, step);

        _client.FinishTestItemAsync(testItem.Uuid, new FinishTestItemRequest
        {
            LaunchUuid = launchUuid,
            Description = BuildFinishDescription(reportCase.StatusDetails),
            EndTime = reportCase.Stop,
            Status = ToReportPortalStatus(reportCase.Status),
            Attributes = BuildCaseAttributes(reportCase)
        }).GetAwaiter().GetResult();
    }

    public override void FinishReport()
    {
        try
        {
            if (_client == null || !_launchStarted || string.IsNullOrWhiteSpace(_launchUuid))
                return;

            _client.FinishLaunchAsync(_launchUuid, new FinishLaunchRequest
            {
                EndTime = DateTime.UtcNow
            }).GetAwaiter().GetResult();
        }
        finally
        {
            _client?.Dispose();
            _client = null;
            _launchUuid = null;
            _launchStarted = false;
        }
    }

    private string EnsureLaunchStarted()
    {
        if (_launchStarted && !string.IsNullOrWhiteSpace(_launchUuid))
            return _launchUuid;

        var config = GetConfig();
        var project = ResolveProjectName();
        _client = _clientFactory.Create(config.Server.Url, project, config.Server.ApiKey);
        var launch = _client.StartLaunchAsync(new StartLaunchRequest
        {
            Name = ResolveLaunchName(project),
            Description = "QaaS Runner execution",
            Mode = config.Launch.DebugMode ? LaunchMode.Debug : LaunchMode.Default,
            StartTime = EpochTestSuiteStartTime,
            Attributes = BuildLaunchAttributes(project)
        }).GetAwaiter().GetResult();

        _launchUuid = launch.Uuid;
        _launchStarted = true;
        return _launchUuid;
    }

    private ReportPortalReporterConfig GetConfig()
    {
        if (_config != null)
            return _config;

        var path = ResolveConfigPath();
        var json = FileSystem.File.ReadAllText(path);
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
            if (FileSystem.File.Exists(candidatePath))
                return candidatePath;
        }

        throw new InvalidOperationException($"Could not find {DefaultConfigFileName}.");
    }

    private string ResolveProjectName()
    {
        var metadata = ResolveMetadata();
        var team = metadata.Team;
        if (string.IsNullOrWhiteSpace(team))
            throw new InvalidOperationException("ReportPortal project must be configured in metadata Team.");

        return team;
    }

    private QaaS.Framework.SDK.MetaDataConfig ResolveMetadata()
    {
        if (Context is not InternalContext internalContext)
            throw new InvalidOperationException("ReportPortal project must be resolved from runner metadata.");

        var metadata = internalContext.GetValueFromGlobalDictionary(internalContext.GetMetaDataPath()) as QaaS.Framework.SDK.MetaDataConfig ??
                       internalContext.GetMetaDataOrDefault();
        return metadata;
    }

    private string ResolveLaunchName(string team)
    {
        var metadata = ResolveMetadata();
        var system = string.IsNullOrWhiteSpace(metadata.System) ? "UnknownSystem" : metadata.System;
        return $"{team} {system} {EpochTestSuiteStartTime:yyyy-MM-dd HH:mm:ss}";
    }

    private List<ItemAttribute> BuildLaunchAttributes(string project)
    {
        var metadata = ResolveMetadata();
        var attributes = new List<ItemAttribute>
        {
            Attribute("tag", QaaSTag),
            Attribute("team", project)
        };

        if (!string.IsNullOrWhiteSpace(metadata.System))
            attributes.Add(Attribute("system", metadata.System));
        if (!string.IsNullOrWhiteSpace(Context.ExecutionId))
            attributes.Add(Attribute("executionId", Context.ExecutionId));
        if (!string.IsNullOrWhiteSpace(Context.CaseName))
            attributes.Add(Attribute("caseName", Context.CaseName));

        return attributes;
    }

    private List<ItemAttribute> BuildCaseAttributes(ReportCase reportCase)
    {
        var metadata = ResolveMetadata();
        var attributes = new List<ItemAttribute>
        {
            Attribute("tag", QaaSTag),
            Attribute("severity", reportCase.Severity.ToString()),
            Attribute("assertionType", reportCase.AssertionType),
            Attribute("flaky", reportCase.IsFlaky.ToString())
        };

        if (!string.IsNullOrWhiteSpace(metadata.System))
            attributes.Add(Attribute("system", metadata.System));
        if (!string.IsNullOrWhiteSpace(Context.ExecutionId))
            attributes.Add(Attribute("executionId", Context.ExecutionId));
        if (!string.IsNullOrWhiteSpace(Context.CaseName))
            attributes.Add(Attribute("caseName", Context.CaseName));

        return attributes;
    }

    private void WriteReportCaseLogs(string itemUuid, ReportCase reportCase)
    {
        WriteLog(itemUuid, "Description", reportCase.Description, RpLogLevel.Info);
        WriteLog(itemUuid, "Status Message", reportCase.StatusDetails.Message, RpLogLevel.Info);
        WriteLog(itemUuid, "Trace", reportCase.StatusDetails.Trace,
            reportCase.Status is AssertionStatus.Failed or AssertionStatus.Broken ? RpLogLevel.Error : RpLogLevel.Info);

        foreach (var link in reportCase.Links)
            WriteLog(itemUuid, $"Link: {link.Name}", link.Url, RpLogLevel.Info);
    }

    private void WriteStep(string parentItemUuid, string launchUuid, ReportStep step)
    {
        var stepItem = _client!.StartChildTestItemAsync(parentItemUuid, new StartTestItemRequest
        {
            LaunchUuid = launchUuid,
            Name = step.Name,
            Description = step.Description,
            StartTime = step.Start ?? EpochTestSuiteStartTime,
            Type = TestItemType.Step,
            HasStats = false,
            Parameters = ToParameters(step.Parameters),
            Attributes =
            [
                Attribute("tag", QaaSTag)
            ]
        }).GetAwaiter().GetResult();

        if (!string.IsNullOrWhiteSpace(step.Description))
            WriteLog(stepItem.Uuid, "Description", step.Description, RpLogLevel.Info);
        WriteAttachments(stepItem.Uuid, step.Attachments);
        foreach (var childStep in step.Steps)
            WriteStep(stepItem.Uuid, launchUuid, childStep);

        _client.FinishTestItemAsync(stepItem.Uuid, new FinishTestItemRequest
        {
            LaunchUuid = launchUuid,
            Description = step.Description,
            EndTime = step.Stop ?? step.Start ?? EpochTestSuiteStartTime,
            Status = ToReportPortalStatus(step.Status)
        }).GetAwaiter().GetResult();
    }

    private void WriteAttachments(string itemUuid, IEnumerable<ReportAttachment> attachments)
    {
        foreach (var attachment in attachments)
        {
            if (attachment.Content == null)
                continue;

            _client!.CreateLogItemAsync(new CreateLogItemRequest
            {
                TestItemUuid = itemUuid,
                Time = DateTime.UtcNow,
                Level = RpLogLevel.Info,
                Text = attachment.Name,
                Attach = new LogItemAttach(attachment.Type, attachment.Content)
                {
                    Name = attachment.FileName
                }
            }).GetAwaiter().GetResult();
        }
    }

    private void WriteLog(string itemUuid, string title, string? text, RpLogLevel level)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        _client!.CreateLogItemAsync(new CreateLogItemRequest
        {
            TestItemUuid = itemUuid,
            Time = DateTime.UtcNow,
            Level = level,
            Text = $"{title}: {text}"
        }).GetAwaiter().GetResult();
    }

    private static IList<KeyValuePair<string, string>> ToParameters(IEnumerable<ReportParameter> parameters)
    {
        return parameters
            .Select(parameter => new KeyValuePair<string, string>(parameter.Name, parameter.Value))
            .ToList();
    }

    private static string BuildFinishDescription(ReportStatusDetails details)
    {
        return string.Join(Environment.NewLine, new[]
        {
            details.Message,
            details.Trace
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static ItemAttribute Attribute(string key, string value)
    {
        return new ItemAttribute
        {
            Key = key,
            Value = value
        };
    }

    private static RpStatus ToReportPortalStatus(AssertionStatus status)
    {
        return status switch
        {
            AssertionStatus.Passed => RpStatus.Passed,
            AssertionStatus.Failed => RpStatus.Failed,
            AssertionStatus.Broken => RpStatus.Failed,
            AssertionStatus.Skipped => RpStatus.Skipped,
            AssertionStatus.Unknown => RpStatus.Info,
            _ => RpStatus.Info
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

internal interface IReportPortalClientFactory
{
    IReportPortalClient Create(string serverUrl, string projectName, string apiKey);
}

internal interface IReportPortalClient : IDisposable
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
