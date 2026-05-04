using System.IO.Abstractions;
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
public class ReportPortalReporter : BaseReporter, ILifecycleReporter
{
    private const string DefaultConfigFileName = "ReportPortal.config.json";
    private readonly IReportPortalClientFactory _clientFactory;
    private readonly IFileSystem _reportPortalFileSystem;
    private readonly string? _configPath;
    private IReportPortalClient? _client;
    private string? _launchUuid;
    private ReportPortalReporterConfig? _config;

    public ReportPortalReporter()
        : this(new ReportPortalClientFactory(), new FileSystem())
    {
    }

    internal ReportPortalReporter(
        IReportPortalClientFactory clientFactory,
        IFileSystem? fileSystem = null,
        string? configPath = null)
    {
        _clientFactory = clientFactory;
        _reportPortalFileSystem = fileSystem ?? new FileSystem();
        _configPath = configPath;
    }

    private bool IsStarted => _client != null && !string.IsNullOrWhiteSpace(_launchUuid);

    private string LaunchUuid => _launchUuid ??
                                 throw new InvalidOperationException("ReportPortal launch has not been started.");

    private IReportPortalClient Client => _client ??
                                          throw new InvalidOperationException("ReportPortal launch has not been started.");
    
    public void StartReport(Context context, DateTime testSuiteStartTimeUtc)
    {
        if (IsStarted)
            return;

        Context = context;
        TestSuiteStartTimeUtc = testSuiteStartTimeUtc;

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

    public void FinishReport()
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

    protected override void WriteReportCase(ReportCase reportCase)
    {
        if (!IsStarted)
        {
            Context.Logger.LogDebug("ReportPortal launch is not started. Skipping ReportPortal report case {ReportCaseName}.",
                reportCase.Name);
            return;
        }

        var launchUuid = LaunchUuid;
        var client = Client;
        var testItem = client.StartTestItemAsync(new StartTestItemRequest
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

        client.FinishTestItemAsync(testItem.Uuid, new FinishTestItemRequest
        {
            LaunchUuid = launchUuid,
            Description = BuildFinishDescription(reportCase.StatusDetails),
            EndTime = reportCase.Stop,
            Status = ToReportPortalStatus(reportCase.Status),
            Attributes = BuildCaseAttributes(reportCase)
        }).GetAwaiter().GetResult();
    }

    private QaaS.Framework.SDK.MetaDataConfig ResolveMetadata()
    {
        return ResolveMetadata(Context);
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
        var client = Client;
        var stepItem = client.StartChildTestItemAsync(parentItemUuid, new StartTestItemRequest
        {
            LaunchUuid = launchUuid,
            Name = step.Name,
            Description = step.Description,
            StartTime = step.Start ?? TestSuiteStartTimeUtc,
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

        client.FinishTestItemAsync(stepItem.Uuid, new FinishTestItemRequest
        {
            LaunchUuid = launchUuid,
            Description = step.Description,
            EndTime = step.Stop ?? step.Start ?? TestSuiteStartTimeUtc,
            Status = ToReportPortalStatus(step.Status)
        }).GetAwaiter().GetResult();
    }

    private void WriteAttachments(string itemUuid, IEnumerable<ReportAttachment> attachments)
    {
        foreach (var attachment in attachments)
        {
            if (attachment.Content == null)
                continue;

            Client.CreateLogItemAsync(new CreateLogItemRequest
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

        Client.CreateLogItemAsync(new CreateLogItemRequest
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

    private ReportPortalReporterConfig GetConfig()
    {
        if (_config != null)
            return _config;

        var path = ResolveConfigPath();
        var json = _reportPortalFileSystem.File.ReadAllText(path);
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
            if (_reportPortalFileSystem.File.Exists(candidatePath))
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
