using System.ComponentModel;
using System.IO.Abstractions;
using QaaS.Framework.Infrastructure;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;
using QaaS.Runner.Assertions.Reporters;
using QaaS.Runner.Assertions.Reporters.Allure;
using QaaS.Runner.Assertions.Reporters.ReportPortal;

namespace QaaS.Runner.Assertions.ConfigurationObjects;

public class ReporterBuilder : ICloneable<ReporterBuilder>
{
    public ReporterBuilder Clone() 
    {
        var clone = BuilderCloner.DeepClone(this);
        clone.ReportPortalLaunchManager = ReportPortalLaunchManager;
        clone.ReportPortalRunDescriptor = ReportPortalRunDescriptor;
        clone.ReportPortal = ReportPortal;
        return clone;
    }
    
    [Description("Whether to save the session logs belonging to the assertion in the test report")]
    public bool? SaveLogs { get; internal set; }

    [Description("Whether to save the attachments of the assertion in the test report (true) or not (false)")]
    public bool? SaveAttachments { get; internal set; }

    [Description("Whether to save the configuration template in the test report (true) or not (false)")]
    public bool? SaveTemplate { get; internal set; }

    [Description("Whether to save the data of the session's belonging to this assertion in the test report")]
    public bool? SaveSessionData { get; internal set; }

    [Description("Whether to display the assertion's message trace in the assertion results or not." +
                 " Should be set to false when the assertion trace is massive and displaying it can cause performance issues")]
    public bool? DisplayTrace { get; internal set; }
    
    [Description("The ReportPortal configuration to use for this reporter. If not set, the default ReportPortal configuration will be used.")]
    public ReportPortalConfig? ReportPortal { get; internal set; } = new();
    
    internal ReportPortalLaunchManager? ReportPortalLaunchManager { get; set; }
    internal ReportPortalLaunchDescriptor? ReportPortalRunDescriptor { get; set; }
    
    /// <summary>
    /// Configure the ReportPortal settings for this reporter. If not set, the default ReportPortal configuration will be used.
    /// </summary>
    public ReporterBuilder ConfigureReportPortal(ReportPortalConfig reportPortalConfig)
    {
        ReportPortal = reportPortalConfig;
        return this;
    }

    internal ReporterBuilder WithReportPortalLaunchManager(ReportPortalLaunchManager manager)
    {
        ReportPortalLaunchManager = manager;
        return this;
    }
    
    internal ReporterBuilder WithReportPortalRunDescriptor(ReportPortalLaunchDescriptor descriptor)
    {
        ReportPortalRunDescriptor = descriptor;
        return this;
    }

    /// <summary>
    /// Configures whether logs are saved with the reporter result.
    /// </summary>
    public ReporterBuilder ShouldSaveLogs(bool shouldSaveLogs)
    {
        SaveLogs = shouldSaveLogs;
        return this;
    }

    /// <summary>
    /// Configures whether attachments are saved with the reporter result.
    /// </summary>
    public ReporterBuilder ShouldSaveAttachments(bool shouldSaveAttachments)
    {
        SaveAttachments = shouldSaveAttachments;
        return this;
    }

    /// <summary>
    /// Configures whether the rendered configuration template is saved with the reporter result.
    /// </summary>
    public ReporterBuilder ShouldSaveTemplate(bool shouldSaveTemplate)
    {
        SaveTemplate = shouldSaveTemplate;
        return this;
    }

    /// <summary>
    /// Configures whether session data is saved with the reporter result.
    /// </summary>
    public ReporterBuilder ShouldSaveSessionData(bool shouldSaveSessionData)
    {
        SaveSessionData = shouldSaveSessionData;
        return this;
    }

    /// <summary>
    /// Configures whether the assertion trace is displayed with the reporter result.
    /// </summary>
    public ReporterBuilder ShouldDisplayTrace(bool shouldDisplayTrace)
    {
        DisplayTrace = shouldDisplayTrace;
        return this;
    }

    
    internal List<IReporter> Build(Context context, DateTime testSuiteStartTimeUtc, IFileSystem? fileSystem = null)
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
                    if (ReportPortal is { Enabled: true } && ReportPortalLaunchManager is not null)
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
                            LaunchManager = ReportPortalLaunchManager,
                            Settings = ReportPortal.Resolve(ReportPortalRunDescriptor)
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
