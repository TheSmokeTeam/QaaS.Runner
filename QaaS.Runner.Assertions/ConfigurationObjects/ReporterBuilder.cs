using System.ComponentModel;
using System.IO.Abstractions;
using QaaS.Framework.Infrastructure;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;
using QaaS.Runner.Assertions.Reporters;
using QaaS.Runner.Assertions.Reporters.Allure;
using QaaS.Runner.Assertions.Reporters.ReportPortal;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace QaaS.Runner.Assertions.ConfigurationObjects;

public class ReporterBuilder : IYamlConvertible, ICloneable<ReporterBuilder>
{
    public ReporterBuilder Clone() => BuilderCloner.DeepClone(this);

    [Description("Whether to save the session logs belonging to the assertions in the test report. " +
                 "If not set, each assertion will determine whether to save them.")]
    [DefaultValue(null)]
    public bool? SaveLogs { get; internal set; }

    [Description("Whether to save the attachments belonging to the assertions in the test report. " +
                 "If not set, each assertion will determine whether to save them.")]
    [DefaultValue(null)]
    public bool? SaveAttachments { get; internal set; }

    [Description("Whether to save the configuration template belonging to the assertions in the test report. " +
                 "If not set, each assertion will determine whether to save it.")]
    [DefaultValue(null)]
    public bool? SaveTemplate { get; internal set; }

    [Description("Whether to save the session data belonging to the assertions in the test report. " +
                 "If not set, each assertion will determine whether to save it.")]
    [DefaultValue(null)]
    public bool? SaveSessionData { get; internal set; }

    [Description("Whether to display the assertion message trace in the assertions results. " +
                 "If not set, each assertion will determine whether to display it.")]
    [DefaultValue(null)]
    public bool? DisplayTrace { get; internal set; }

    [Description("ReportPortal configuration to use for this reporter. " +
                 "If not set, the default ReportPortal configuration will be used.")]
    [DefaultValue(typeof(ReportPortalConfig))]
    public ReportPortalConfig? ReportPortal { get; internal set; } = new();
    
    public void Read(IParser parser, Type expectedType, ObjectDeserializer nestedObjectDeserializer)
    {
        throw new NotSupportedException($"{nameof(Read)} doesn't support custom" +
                                        $" deserialization from Yaml for {nameof(ReporterBuilder)}");
    }
    
    public void Write(IEmitter emitter, ObjectSerializer nestedObjectSerializer)
    {
        nestedObjectSerializer(new
        {
            SaveLogs,
            SaveAttachments,
            SaveTemplate,
            SaveSessionData,
            DisplayTrace,
            ReportPortal
        });
    }
    
    /// <summary>
    /// Sets the ReportPortal configuration used when creating a ReportPortal reporter.
    /// </summary>
    public ReporterBuilder ConfigureReportPortal(ReportPortalConfig reportPortalConfig)
    {
        ReportPortal = reportPortalConfig;
        return this;
    }

    /// <summary>
    /// Configures whether assertion session logs are saved with reporter results.
    /// </summary>
    public ReporterBuilder ShouldSaveLogs(bool shouldSaveLogs)
    {
        SaveLogs = shouldSaveLogs;
        return this;
    }

    /// <summary>
    /// Configures whether assertion attachments are saved with reporter results.
    /// </summary>
    public ReporterBuilder ShouldSaveAttachments(bool shouldSaveAttachments)
    {
        SaveAttachments = shouldSaveAttachments;
        return this;
    }

    /// <summary>
    /// Configures whether the rendered assertion configuration template is saved with reporter results.
    /// </summary>
    public ReporterBuilder ShouldSaveTemplate(bool shouldSaveTemplate)
    {
        SaveTemplate = shouldSaveTemplate;
        return this;
    }

    /// <summary>
    /// Configures whether assertion session data is saved with reporter results.
    /// </summary>
    public ReporterBuilder ShouldSaveSessionData(bool shouldSaveSessionData)
    {
        SaveSessionData = shouldSaveSessionData;
        return this;
    }

    /// <summary>
    /// Configures whether the assertion message trace is displayed in reporter results.
    /// </summary>
    public ReporterBuilder ShouldDisplayTrace(bool shouldDisplayTrace)
    {
        DisplayTrace = shouldDisplayTrace;
        return this;
    }
    
    /// <summary>
    /// Builds the reporter instances that should publish assertion results for the current run.
    /// </summary>
    /// <remarks>
    /// Allure is always created. ReportPortal is created only when its resolved configuration is enabled.
    /// </remarks>
    /// <param name="context">The QaaS execution context to attach to each reporter.</param>
    /// <param name="testSuiteStartTimeUtc">
    /// The UTC test-suite start time used by reporters that need an epoch-based run timestamp.
    /// </param>
    /// <param name="fileSystem">
    /// Optional file-system abstraction used by reporters. When omitted, a default <see cref="FileSystem"/> is used.
    /// </param>
    /// <param name="executionMode">The execution mode represented by the current execution.</param>
    /// <returns>The configured reporters for the current assertion run.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when an unsupported <see cref="ReporterTarget"/> value is encountered.
    /// </exception>
    internal List<IReporter> Build(Context context, 
        DateTime testSuiteStartTimeUtc, 
        IFileSystem? fileSystem = null,
        string executionMode = "run")
    {
        var reporters = new List<IReporter>();
        
        foreach (var target in Enum.GetValues<ReporterTarget>())
        {
            switch (target)
            {
                case ReporterTarget.Allure:
                    var allureReporter = new AllureReporter
                    {
                        Context = context,
                        DisplayTrace = DisplayTrace,
                        SaveLogs = SaveLogs,
                        SaveAttachments = SaveAttachments,
                        SaveTemplate = SaveTemplate,
                        SaveSessionData = SaveSessionData,
                        FileSystem = fileSystem ?? new FileSystem(),
                        EpochTestSuiteStartTime = new DateTimeOffset(testSuiteStartTimeUtc).ToUnixTimeMilliseconds()
                    };
                    reporters.Add(allureReporter);
                    break;

                case ReporterTarget.ReportPortal:
                    if (ReportPortal is { Enabled: true } reportPortalConfig)
                    {
                        var reportPortalReporter = new ReportPortalReporter
                        {
                            Context = context,
                            DisplayTrace = DisplayTrace,
                            SaveLogs = SaveLogs,
                            SaveAttachments = SaveAttachments,
                            SaveTemplate = SaveTemplate,
                            SaveSessionData = SaveSessionData,
                            FileSystem = fileSystem ?? new FileSystem(),
                            Config = reportPortalConfig,
                            ExecutionMode = executionMode
                        };
                        reporters.Add(reportPortalReporter);
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported reporter target.");
            }
        }
        
        return reporters;
    }
}
