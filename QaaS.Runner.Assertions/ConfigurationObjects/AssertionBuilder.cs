using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using QaaS.Framework.Configurations;
using QaaS.Framework.Configurations.ConfigurationBindingUtils;
using QaaS.Framework.Configurations.CustomValidationAttributes;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using Assertion = QaaS.Runner.Assertions.AssertionObjects.Assertion;
using AssertionSeverity = QaaS.Runner.Assertions.AssertionObjects.AssertionSeverity;

[assembly: InternalsVisibleTo("QaaS.Runner")]
[assembly: InternalsVisibleTo("QaaS.Runner.Assertions.Tests")]
[assembly: InternalsVisibleTo("QaaS.Runner.Tests")]

namespace QaaS.Runner.Assertions.ConfigurationObjects;

/// <summary>
/// Builder for configuring and creating assertion instances
/// </summary>
public class AssertionBuilder : IYamlConvertible
{
    /// <summary>
    /// Internal assertion instance
    /// </summary>
    public required Assertion AssertionInstance;

    /// <summary>
    /// Internal reporter instance
    /// </summary>
    public required BaseReporter Reporter;

    [Required]
    [Description("The name of the assertion to use")]
    public string? Assertion { get; internal set; }
    [Required, Description("The name of the test as presented in the test report with this assertion's result, if none is " +
                           "given creates a name as the type of the assertion and guid")]
    public string? Name { get; internal set; }

    [Description("The category of the assersion. Can filter which categories to run using the -A flag")]
    public string? Category { get; internal set; }
    [RequiredIfAny(nameof(SessionNamePatterns), [null])]
    [Description("The names of session datas to give the assertion")]
    public string[]? SessionNames { get; internal set; } = [];
    [RequiredIfAny(nameof(SessionNames), [null])]
    [Description("Regex patterns of session names")]
    public string[]? SessionNamePatterns { get; internal set; } = [];
    [Description("Names of the pre defined data sources to pass to the assertion")]
    public string[] DataSourceNames { get; internal set; } = [];
    [Description("Regex patterns of data sources")]
    public string[] DataSourcePatterns { get; internal set; } = [];
    [Description("Whether to save the data of the session's belonging to this assertion in the test report")]
    [DefaultValue(true)]
    public bool SaveSessionData { get; internal set; } = true;
    [Description("Whether to save the session logs belonging to this assertion in the test report")]
    [DefaultValue(true)]
    public bool SaveLogs { get; internal set; } = true;
    [Description("Whether to save the attachments of the assertion in the test report (true) or not (false)")]
    [DefaultValue(true)]
    public bool SaveAttachments { get; internal set; } = true;
    [Description("Whether to save the configuration template in the test report (true) or not (false)")]
    [DefaultValue(true)]
    public bool SaveTemplate { get; internal set; } = true;
    [Description("Whether to display the assertion's message trace in the assertion results or not." +
                 " Should be set to false when the assertion trace is massive and displaying it can cause performance issues")]
    [DefaultValue(true)]
    public bool DisplayTrace { get; internal set; } = true;
    [Description("The severity of the assertion, can be used to set the severity of the test in the test report.")]
    [DefaultValue(AssertionSeverity.Normal)]
    public AssertionSeverity Severity { get; internal set; } = AssertionSeverity.Normal;
    [Description("Implementation configuration for the assertion, " +
                 "the configuration given here is loaded into the provided assertion dynamically.")]
    public IConfiguration AssertionConfiguration { get; internal set; } = new ConfigurationBuilder().Build();
    public IConfiguration Configuration
    {
        get => AssertionConfiguration;
        internal set => AssertionConfiguration = value ?? new ConfigurationBuilder().Build();
    }
    [Description("The assertion's specific links. Will be added with the general links.")]
    public List<LinkBuilder> Links { get; internal set; } = [];
    /// <summary>
    /// Defines which assertion statuses will appear in the final report.
    /// Statuses explicitly listed will be included in the report, while all others will be excluded.
    /// </summary>
    [Description("Defines which assertion statuses will appear in the final report. " +
                 "Statuses explicitly listed will be included in the report, while all others will be exluded." +
                 $"Options: [`{nameof(AssertionStatus.Passed)}` `{nameof(AssertionStatus.Broken)}` `{nameof(AssertionStatus.Failed)}` `{nameof(AssertionStatus.Skipped)}` `{nameof(AssertionStatus.Unknown)}` ]"),
     DefaultValue("List containing all assertion statuses")]
    public IList<AssertionStatus> StatusesToReport { get; set; } = Enum.GetValues<AssertionStatus>().ToList();

    /// <summary>
    /// Reads the serialized configuration for the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// This method participates in the YAML serialization surface that backs configuration-as-code support.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public void Read(IParser parser, Type expectedType, ObjectDeserializer nestedObjectDeserializer)
    {
        throw new NotSupportedException($"{nameof(Read)} doesn't support custom" +
                                        $" deserialization from Yaml for {nameof(AssertionBuilder)}");
    }

    /// <summary>
    /// Writes the current Runner assertion builder configuration to the configured serializer output.
    /// </summary>
    /// <remarks>
    /// This method participates in the YAML serialization surface that backs configuration-as-code support.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public void Write(IEmitter emitter, ObjectSerializer nestedObjectSerializer)
    {
        var assertionConfiguration = AssertionConfiguration
            .GetDictionaryFromConfiguration();
        nestedObjectSerializer(new
        {
            Assertion,
            SessionNames,
            DataSourceNames,
            SaveSessionData,
            SaveLogs,
            SaveAttachments,
            Name,
            DisplayTrace,
            AssertionConfiguration = assertionConfiguration
        });
    }

    /// <summary>
    /// Sets which assertion statuses should be included in reports.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder ReportOnlyStatuses(IList<AssertionStatus> statusesToReport)
    {
        StatusesToReport = statusesToReport;
        return this;
    }

    /// <summary>
    /// Configures category on the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder WithCategory(string category)
    {
        Category = category;
        return this;
    }

    /// <summary>
    /// Configures whether session data is saved with the assertion result.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder ShouldSaveSessionData(bool shouldSaveSessionData)
    {
        SaveSessionData = shouldSaveSessionData;
        return this;
    }

    internal AssertionBuilder WeatherToSaveSessionData(bool weatherToSaveSessionData) =>
        ShouldSaveSessionData(weatherToSaveSessionData);

    /// <summary>
    /// Configures whether logs are saved with the assertion result.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder ShouldSaveLogs(bool shouldSaveLogs)
    {
        SaveLogs = shouldSaveLogs;
        return this;
    }

    internal AssertionBuilder WeatherToSaveLogs(bool weatherToSaveLogs) => ShouldSaveLogs(weatherToSaveLogs);
    
    /// <summary>
    /// Configures whether the rendered configuration template is saved with the assertion result.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder ShouldSaveConfigurationTemplate(bool shouldSaveConfigurationTemplate)
    {
        SaveTemplate = shouldSaveConfigurationTemplate;
        return this;
    }

    internal AssertionBuilder WeatherToSaveConfigurationTemplate(bool weatherToSaveConfigurationTemplate) =>
        ShouldSaveConfigurationTemplate(weatherToSaveConfigurationTemplate);

    /// <summary>
    /// Sets the severity associated with the assertion result.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder WithSeverity(AssertionSeverity severity)
    {
        Severity = severity;
        return this;
    }
    
    /// <summary>
    /// Configures whether attachments are saved with the assertion result.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder ShouldSaveAttachments(bool shouldSaveAttachments)
    {
        SaveAttachments = shouldSaveAttachments;
        return this;
    }

    internal AssertionBuilder WeatherToSaveAttachments(bool weatherToSaveAttachments) =>
        ShouldSaveAttachments(weatherToSaveAttachments);
    
    /// <summary>
    /// Configures whether the assertion trace is displayed with the result.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The behavior exposed here is part of the public surface that the generated function documentation groups under 'Configuration as Code / Assertions'.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder ShouldDisplayTrace(bool shouldDisplayTrace)
    {
        DisplayTrace = shouldDisplayTrace;
        return this;
    }

    internal AssertionBuilder WeatherToDisplayTrace(bool weatherToDisplayTrace) =>
        ShouldDisplayTrace(weatherToDisplayTrace);
    
    /// <summary>
    /// Sets the name used for the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    /// <summary>
    /// Sets the hook implementation name used by the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder HookNamed(string hookName)
    {
        Assertion = hookName;
        return this;
    }

    /// <summary>
    /// Adds the supplied data source name to the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder AddDataSourceName(string dataSourceName)
    {
        DataSourceNames = (DataSourceNames ?? []).Append(dataSourceName).ToArray();
        return this;
    }

    /// <summary>
    /// Removes the configured data source name from the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder RemoveDataSourceName(string dataSourceName)
    {
        DataSourceNames = (DataSourceNames ?? []).Where(value => value != dataSourceName).ToArray();
        return this;
    }

    /// <summary>
    /// Removes the configured data source name at the specified index from the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder RemoveDataSourceNameAt(int index)
    {
        DataSourceNames = RemoveAt(DataSourceNames, index) ?? [];
        return this;
    }

    /// <summary>
    /// Adds the supplied data source pattern to the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder AddDataSourcePattern(string dataSourcePattern)
    {
        DataSourcePatterns = (DataSourcePatterns ?? []).Append(dataSourcePattern).ToArray();
        return this;
    }

    /// <summary>
    /// Removes the configured data source pattern from the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder RemoveDataSourcePattern(string dataSourcePattern)
    {
        DataSourcePatterns = (DataSourcePatterns ?? []).Where(value => value != dataSourcePattern).ToArray();
        return this;
    }

    /// <summary>
    /// Removes the configured data source pattern at the specified index from the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder RemoveDataSourcePatternAt(int index)
    {
        DataSourcePatterns = RemoveAt(DataSourcePatterns, index) ?? [];
        return this;
    }

    /// <summary>
    /// Adds the supplied session name to the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder AddSessionName(string sessionName)
    {
        SessionNames = SessionNames == null ? [sessionName] : SessionNames.Append(sessionName).ToArray();
        return this;
    }

    /// <summary>
    /// Removes the configured session name from the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder RemoveSessionName(string sessionName)
    {
        SessionNames = SessionNames?.Where(value => value != sessionName).ToArray();
        return this;
    }

    /// <summary>
    /// Removes the configured session name at the specified index from the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder RemoveSessionNameAt(int index)
    {
        SessionNames = RemoveAt(SessionNames, index);
        return this;
    }

    /// <summary>
    /// Adds the supplied session pattern to the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder AddSessionPattern(string sessionPattern)
    {
        SessionNamePatterns = SessionNamePatterns == null
            ? [sessionPattern]
            : SessionNamePatterns.Append(sessionPattern).ToArray();
        return this;
    }

    /// <summary>
    /// Removes the configured session pattern from the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder RemoveSessionPattern(string sessionPattern)
    {
        SessionNamePatterns = SessionNamePatterns?.Where(value => value != sessionPattern).ToArray();
        return this;
    }

    /// <summary>
    /// Removes the configured session pattern at the specified index from the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder RemoveSessionPatternAt(int index)
    {
        SessionNamePatterns = RemoveAt(SessionNamePatterns, index);
        return this;
    }

    /// <summary>
    /// Adds the supplied link to the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder AddLink(LinkBuilder linkBuilder)
    {
        Links = (Links ?? []).Append(linkBuilder).ToList();
        return this;
    }

    /// <summary>
    /// Removes the configured link from the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder RemoveLink(string name)
    {
        Links = (Links ?? []).Where(link => link.Name != name).ToList();
        return this;
    }

    /// <summary>
    /// Removes the configured link at the specified index from the current Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder RemoveLinkAt(int index)
    {
        var links = Links ?? [];
        if (index < 0 || index >= links.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        links.RemoveAt(index);
        Links = links;
        return this;
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder Configure(object configuration)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(configuration)));
        AssertionConfiguration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        return this;
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder UpdateConfiguration(object configuration)
    {
        AssertionConfiguration = (AssertionConfiguration ?? new ConfigurationBuilder().Build())
            .UpdateConfiguration(configuration);
        return this;
    }

    /// <summary>
    /// Clears the configuration currently stored on the Runner assertion builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner assertion builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Assertions" />
    public AssertionBuilder RemoveConfiguration()
    {
        AssertionConfiguration = new ConfigurationBuilder().Build();
        return this;
    }

    private static T[]? RemoveAt<T>(T[]? values, int index)
    {
        if (values == null)
        {
            return null;
        }

        if (index < 0 || index >= values.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return values.Where((_, i) => i != index).ToArray();
    }

    /// <summary>
    /// Binds a configured assertion hook and merges local/global link builders into runtime links.
    /// </summary>
    internal Assertion Build(IList<KeyValuePair<string, IAssertion>> assertions, IEnumerable<LinkBuilder>? linkBuilders)
    {
        AssertionInstance = new Assertion
        {
            AssertionConfiguration = AssertionConfiguration,
            _sessionNames = SessionNames,
            _sessionPatterns = SessionNamePatterns,
            _dataSourcePatterns = DataSourcePatterns,
            _dataSourceNames = DataSourceNames,
            Name = Name!,
            SaveSessionData = SaveSessionData,
            SaveLogs = SaveLogs,
            SaveAttachments = SaveAttachments,
            SaveTemplate = SaveTemplate,
            DisplayTrace = DisplayTrace,
            Severity = Severity,
            ReporterType = GetReporterType(),
            AssertionHook = assertions.FirstOrDefault(pair => pair.Key == Name!)
                                .Value ??
                            throw new ArgumentException($"Assertion {Name} of type" +
                                                        $" {Assertion} was not found" +
                                                        " in provided assertions."),
            StatussesToReport = StatusesToReport,
            AssertionName = string.Empty
        };

        var allLinkBuilders = Links.Concat(linkBuilders ?? []).ToList();
        var allLinks = allLinkBuilders.Select(linkBuilder => linkBuilder.Build());
        
        AssertionInstance.AssertionName = Assertion!;
        AssertionInstance.Links = allLinks.ToList();
        return AssertionInstance;
    }

    internal Type GetReporterType() => Reporter?.GetType() ?? typeof(AllureReporter);

    /// <summary>
    ///     Resolves the pre-built <see cref="BaseReporter" /> to a runtime object with access to QaaS'
    ///     <see cref="Context" /> and finalize building the Reporter.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="testSuiteStartTimeUtc"></param>
    /// <param name="fileSystem"></param>
    /// <returns></returns>
    internal IReporter Build(Context context, DateTime testSuiteStartTimeUtc,
        IFileSystem? fileSystem = null)
    {
        Reporter = (BaseReporter?)Activator.CreateInstance(GetReporterType()) ??
                   throw new InvalidOperationException(
                       $"Could not create reporter of type {GetReporterType().FullName}.");

        Reporter.Name = GetReporterType().Name;
        Reporter.AssertionName = Name!;

        Reporter.DisplayTrace = DisplayTrace;
        Reporter.SaveSessionData = SaveSessionData;
        Reporter.SaveLogs = SaveLogs;
        Reporter.SaveAttachments = SaveAttachments;
        Reporter.SaveTemplate = SaveTemplate;
        Reporter.Severity = Severity;
        Reporter.Context = context;
        Reporter.EpochTestSuiteStartTime =
            new DateTimeOffset(testSuiteStartTimeUtc, new TimeSpan(0)).ToUnixTimeMilliseconds();

        Reporter.FileSystem = fileSystem ?? new FileSystem();
        return Reporter;
    }

    internal IEnumerable<IReporter> BuildReporters(Context context, DateTime testSuiteStartTimeUtc,
        IFileSystem? fileSystem = null)
    {
        yield return Build(context, testSuiteStartTimeUtc, fileSystem);
    }
}
