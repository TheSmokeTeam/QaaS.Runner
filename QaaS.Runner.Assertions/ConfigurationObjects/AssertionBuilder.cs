using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
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
    internal string? Assertion { get; set; }
    
    [Required, Description("The name of the test as presented in the test report with this assertion's result, if none is " +
                           "given creates a name as the type of the assertion and guid")]
    public string? Name { get; internal set; }

    [Description("The category of the assersion. Can filter which categories to run using the -A flag")]
    internal string? Category { get; set; }

    [RequiredIfAny(nameof(SessionNamePatterns), [null])]
    [Description("The names of session datas to give the assertion")]
    internal string[]? SessionNames { get; set; } = [];

    [RequiredIfAny(nameof(SessionNames), [null])]
    [Description("Regex patterns of session names")]
    internal string[]? SessionNamePatterns { get; set; } = [];

    [Description("Names of the pre defined data sources to pass to the assertion")]
    internal string[] DataSourceNames { get; set; } = [];

    [Description("Regex patterns of data sources")]
    internal string[] DataSourcePatterns { get; set; } = [];

    [Description("Whether to save the data of the session's belonging to this assertion in the test report")]
    [DefaultValue(true)]
    internal bool SaveSessionData { get; set; } = true;

    [Description("Whether to save the attachments of the assertion in the test report (true) or not (false)")]
    [DefaultValue(true)]
    internal bool SaveAttachments { get; set; } = true;
    
    [Description("Whether to save the configuration template in the test report (true) or not (false)")]
    [DefaultValue(true)]
    internal bool SaveTemplate { get; set; } = true;

    [Description("Whether to display the assertion's message trace in the assertion results or not." +
                 " Should be set to false when the assertion trace is massive and displaying it can cause performance issues")]
    [DefaultValue(true)]
    internal bool DisplayTrace { get; set; } = true;

    [Description("The severity of the assertion, can be used to set the severity of the test in the test report.")]
    [DefaultValue(AssertionSeverity.Normal)]
    internal AssertionSeverity Severity { get; set; } = AssertionSeverity.Normal;

    [Description("Implementation configuration for the assertion, " +
                 "the configuration given here is loaded into the provided assertion dynamically.")]
    internal IConfiguration AssertionConfiguration { get; set; } = new ConfigurationBuilder().Build();

    [Description("The assertion's specific links. Will be added with the general links.")]
    internal List<LinkBuilder> Links { get; set; } = [];
    
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
    /// Reads YAML configuration (not supported for AssertionBuilder)
    /// </summary>
    public void Read(IParser parser, Type expectedType, ObjectDeserializer nestedObjectDeserializer)
    {
        throw new NotSupportedException($"{nameof(Read)} doesn't support custom" +
                                        $" deserialization from Yaml for {nameof(AssertionBuilder)}");
    }

    /// <summary>
    /// Writes the assertion builder configuration to YAML emitter
    /// </summary>
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
            SaveAttachments,
            Name,
            DisplayTrace,
            AssertionConfiguration = assertionConfiguration
        });
    }

    /// <summary>
    /// Sets which assertion statuses should be reported
    /// </summary>
    /// <param name="statusesToReport">List of assertion statuses to include in the report</param>
    /// <returns>This builder instance for chaining</returns>
    public AssertionBuilder ReportOnlyStatuses(IList<AssertionStatus> statusesToReport)
    {
        StatusesToReport = statusesToReport;
        return this;
    }

    /// <summary>
    /// Sets the category for the assertion
    /// </summary>
    /// <param name="category">Category name</param>
    /// <returns>This builder instance for chaining</returns>
    public AssertionBuilder WithCategory(string category)
    {
        Category = category;
        return this;
    }

    /// <summary>
    /// Sets whether to save session data
    /// </summary>
    /// <param name="weatherToSaveSessionData">Whether to save session data</param>
    /// <returns>This builder instance for chaining</returns>
    public AssertionBuilder WeatherToSaveSessionData(bool weatherToSaveSessionData)
    {
        SaveSessionData = weatherToSaveSessionData;
        return this;
    }
    
    /// <summary>
    /// Sets whether to save configuration template
    /// </summary>
    /// <param name="weatherToSaveConfigurationTemplate">Whether to save configuration template</param>
    /// <returns>This builder instance for chaining</returns>
    public AssertionBuilder WeatherToSaveConfigurationTemplate(bool weatherToSaveConfigurationTemplate)
    {
        SaveTemplate = weatherToSaveConfigurationTemplate;
        return this;
    }

    /// <summary>
    /// Sets the severity level for the assertion
    /// </summary>
    /// <param name="severity">Severity level</param>
    /// <returns>This builder instance for chaining</returns>
    public AssertionBuilder WithSeverity(AssertionSeverity severity)
    {
        Severity = severity;
        return this;
    }
    
    /// <summary>
    /// Sets whether to save attachments
    /// </summary>
    /// <param name="weatherToSaveAttachments">Whether to save attachments</param>
    /// <returns>This builder instance for chaining</returns>
    public AssertionBuilder WeatherToSaveAttachments(bool weatherToSaveAttachments)
    {
        SaveAttachments = weatherToSaveAttachments;
        return this;
    }
    
    /// <summary>
    /// Sets whether to display assertion trace
    /// </summary>
    /// <param name="weatherToDisplayTrace">Whether to display trace</param>
    /// <returns>This builder instance for chaining</returns>
    public AssertionBuilder WeatherToDisplayTrace(bool weatherToDisplayTrace)
    {
        DisplayTrace = weatherToDisplayTrace;
        return this;
    }
    
    /// <summary>
    /// Sets the display name for the assertion
    /// </summary>
    /// <param name="name">Display name</param>
    /// <returns>This builder instance for chaining</returns>
    public AssertionBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    /// <summary>
    /// Sets the assertion hook implementation name
    /// </summary>
    /// <param name="hookName">Hook implementation name</param>
    /// <returns>This builder instance for chaining</returns>
    public AssertionBuilder HookNamed(string hookName)
    {
        Assertion = hookName;
        return this;
    }

    /// <summary>
    /// Adds a data source name filter
    /// </summary>
    /// <param name="dataSourceName">Data source name to add</param>
    /// <returns>This builder instance for chaining</returns>
    public AssertionBuilder AddDataSourceName(string dataSourceName)
    {
        DataSourceNames = DataSourceNames.Append(dataSourceName).ToArray();
        return this;
    }

    public AssertionBuilder CreateDataSourceName(string dataSourceName)
    {
        return AddDataSourceName(dataSourceName);
    }

    public IReadOnlyList<string> ReadDataSourceNames()
    {
        return DataSourceNames;
    }

    public AssertionBuilder UpdateDataSourceName(string existingValue, string newValue)
    {
        var index = Array.IndexOf(DataSourceNames, existingValue);
        if (index >= 0)
        {
            DataSourceNames[index] = newValue;
        }

        return this;
    }

    public AssertionBuilder DeleteDataSourceName(string dataSourceName)
    {
        DataSourceNames = DataSourceNames.Where(value => value != dataSourceName).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a data source pattern filter
    /// </summary>
    /// <param name="dataSourcePattern">Regex pattern for data source names</param>
    /// <returns>This builder instance for chaining</returns>
    public AssertionBuilder AddDataSourcePattern(string dataSourcePattern)
    {
        DataSourcePatterns = DataSourcePatterns.Append(dataSourcePattern).ToArray();
        return this;
    }

    public AssertionBuilder CreateDataSourcePattern(string dataSourcePattern)
    {
        return AddDataSourcePattern(dataSourcePattern);
    }

    public IReadOnlyList<string> ReadDataSourcePatterns()
    {
        return DataSourcePatterns;
    }

    public AssertionBuilder UpdateDataSourcePattern(string existingValue, string newValue)
    {
        var index = Array.IndexOf(DataSourcePatterns, existingValue);
        if (index >= 0)
        {
            DataSourcePatterns[index] = newValue;
        }

        return this;
    }

    public AssertionBuilder DeleteDataSourcePattern(string dataSourcePattern)
    {
        DataSourcePatterns = DataSourcePatterns.Where(value => value != dataSourcePattern).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a session name filter
    /// </summary>
    /// <param name="sessionName">Session name to add</param>
    /// <returns>This builder instance for chaining</returns>
    public AssertionBuilder AddSessionName(string sessionName)
    {
        SessionNames = SessionNames == null ? [sessionName] : SessionNames.Append(sessionName).ToArray();
        return this;
    }

    public AssertionBuilder CreateSessionName(string sessionName)
    {
        return AddSessionName(sessionName);
    }

    public IReadOnlyList<string> ReadSessionNames()
    {
        return SessionNames ?? [];
    }

    public AssertionBuilder UpdateSessionName(string existingValue, string newValue)
    {
        if (SessionNames == null)
        {
            return this;
        }

        var index = Array.IndexOf(SessionNames, existingValue);
        if (index >= 0)
        {
            SessionNames[index] = newValue;
        }

        return this;
    }

    public AssertionBuilder DeleteSessionName(string sessionName)
    {
        SessionNames = SessionNames?.Where(value => value != sessionName).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a session name pattern filter
    /// </summary>
    /// <param name="sessionPattern">Regex pattern for session names</param>
    /// <returns>This builder instance for chaining</returns>
    public AssertionBuilder AddSessionPattern(string sessionPattern)
    {
        SessionNamePatterns = SessionNamePatterns == null
            ? [sessionPattern]
            : SessionNamePatterns.Append(sessionPattern).ToArray();
        return this;
    }

    public AssertionBuilder CreateSessionPattern(string sessionPattern)
    {
        return AddSessionPattern(sessionPattern);
    }

    public IReadOnlyList<string> ReadSessionPatterns()
    {
        return SessionNamePatterns ?? [];
    }

    public AssertionBuilder UpdateSessionPattern(string existingValue, string newValue)
    {
        if (SessionNamePatterns == null)
        {
            return this;
        }

        var index = Array.IndexOf(SessionNamePatterns, existingValue);
        if (index >= 0)
        {
            SessionNamePatterns[index] = newValue;
        }

        return this;
    }

    public AssertionBuilder DeleteSessionPattern(string sessionPattern)
    {
        SessionNamePatterns = SessionNamePatterns?.Where(value => value != sessionPattern).ToArray();
        return this;
    }

    /// <summary>
    /// Adds a link builder to the assertion
    /// </summary>
    /// <param name="linkBuilder">Link builder to add</param>
    /// <returns>This builder instance for chaining</returns>
    public AssertionBuilder AddLink(LinkBuilder linkBuilder)
    {
        Links = Links.Append(linkBuilder).ToList();
        return this;
    }

    public AssertionBuilder CreateLink(LinkBuilder linkBuilder)
    {
        return AddLink(linkBuilder);
    }

    public IReadOnlyList<LinkBuilder> ReadLinks()
    {
        return Links;
    }

    public AssertionBuilder UpdateLink(string name, LinkBuilder linkBuilder)
    {
        var index = Links.FindIndex(configuredLink => configuredLink.Name == name);
        if (index >= 0)
        {
            Links[index] = linkBuilder;
        }

        return this;
    }

    public AssertionBuilder DeleteLink(string name)
    {
        Links = Links.Where(link => link.Name != name).ToList();
        return this;
    }

    /// <summary>
    /// Configures the assertion with a configuration object
    /// </summary>
    /// <param name="configuration">Configuration object to use</param>
    /// <returns>This builder instance for chaining</returns>
    public AssertionBuilder Configure(object configuration)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(configuration)));
        AssertionConfiguration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        return this;
    }

    public IConfiguration ReadConfiguration()
    {
        return AssertionConfiguration;
    }

    public AssertionBuilder UpdateConfiguration(object configuration)
    {
        AssertionConfiguration = AssertionConfiguration.BindConfigurationObjectToIConfiguration(configuration);
        return this;
    }

    public AssertionBuilder UpsertConfiguration(object configuration)
    {
        return UpdateConfiguration(configuration);
    }

    public AssertionBuilder DeleteConfiguration()
    {
        AssertionConfiguration = new ConfigurationBuilder().Build();
        return this;
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
        Reporter = new AllureReporter
        {
            Name = Name!,
            AssertionName = Name!
        };

        Reporter.DisplayTrace = DisplayTrace;
        Reporter.SaveSessionData = SaveSessionData;
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
