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

public class AssertionBuilder : IYamlConvertible
{
    private Assertion _assertion;

    private BaseReporter _reporter;

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
    
    [Description("Defines which assertion statuses will appear in the final report. " +
                 "Statuses explicitly listed will be included in the report, while all others will be exluded." +
                 $"Options: [`{nameof(AssertionStatus.Passed)}` `{nameof(AssertionStatus.Broken)}` `{nameof(AssertionStatus.Failed)}` `{nameof(AssertionStatus.Skipped)}` `{nameof(AssertionStatus.Unknown)}` ]"),
     DefaultValue("List containing all assertion statuses")]
    public IList<AssertionStatus> StatusesToReport { get; set; } = Enum.GetValues<AssertionStatus>().ToList();

    public void Read(IParser parser, Type expectedType, ObjectDeserializer nestedObjectDeserializer)
    {
        throw new NotSupportedException($"{nameof(Read)} doesn't support custom" +
                                        $" deserialization from Yaml for {nameof(AssertionBuilder)}");
    }

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

    public AssertionBuilder ReportOnlyStatuses(IList<AssertionStatus> statusesToReport)
    {
        StatusesToReport = statusesToReport;
        return this;
    }

    public AssertionBuilder WithCategory(string category)
    {
        Category = category;
        return this;
    }

    public AssertionBuilder WeatherToSaveSessionData(bool weatherToSaveSessionData)
    {
        SaveSessionData = weatherToSaveSessionData;
        return this;
    }
    
    public AssertionBuilder WeatherToSaveConfigurationTemplate(bool weatherToSaveConfigurationTemplate)
    {
        SaveTemplate = weatherToSaveConfigurationTemplate;
        return this;
    }

    public AssertionBuilder WithSeverity(AssertionSeverity severity)
    {
        Severity = severity;
        return this;
    }
    
    public AssertionBuilder WeatherToSaveAttachments(bool weatherToSaveAttachments)
    {
        SaveAttachments = weatherToSaveAttachments;
        return this;
    }
    
    public AssertionBuilder WeatherToDisplayTrace(bool weatherToDisplayTrace)
    {
        DisplayTrace = weatherToDisplayTrace;
        return this;
    }
    
    public AssertionBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    public AssertionBuilder HookNamed(string hookName)
    {
        Assertion = hookName;
        return this;
    }

    public AssertionBuilder AddDataSourceName(string dataSourceName)
    {
        DataSourceNames = DataSourceNames.Append(dataSourceName).ToArray();
        return this;
    }

    public AssertionBuilder AddDataSourcePattern(string dataSourcePattern)
    {
        DataSourcePatterns = DataSourcePatterns.Append(dataSourcePattern).ToArray();
        return this;
    }

    public AssertionBuilder AddSessionName(string sessionName)
    {
        SessionNames = SessionNames != null ? [sessionName] : SessionNames!.Append(sessionName).ToArray();
        return this;
    }

    public AssertionBuilder AddSessionPattern(string sessionPattern)
    {
        SessionNamePatterns = SessionNamePatterns != null
            ? [sessionPattern]
            : SessionNamePatterns!.Append(sessionPattern).ToArray();
        return this;
    }

    public AssertionBuilder AddLink(LinkBuilder linkBuilder)
    {
        Links = Links.Append(linkBuilder).ToList();
        return this;
    }

    public AssertionBuilder Configure(object configuration)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(configuration)));
        AssertionConfiguration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        return this;
    }

    internal Assertion Build(IList<KeyValuePair<string, IAssertion>> assertions, IEnumerable<LinkBuilder>? linkBuilders)
    {
        _assertion = new Assertion
        {
            AssertionConfiguration = AssertionConfiguration,
            _sessionNames = SessionNames,
            _sessionPatterns = SessionNamePatterns,
            _dataSourcePatterns = DataSourcePatterns,
            _dataSourceNames = DataSourceNames,
            Name = Name!,
            AssertionHook = assertions.FirstOrDefault(pair => pair.Key == Name!).Value
                            ?? throw new ArgumentException($"Assertion {Name} of type" +
                                                           $" {Assertion} was not found" +
                                                           " in provided assertions."),
            StatussesToReport = StatusesToReport
        };

        var allLinkBuilders = Links.Concat(linkBuilders ?? []).ToList();
        var allLinks = allLinkBuilders.Select(linkBuilder => linkBuilder.Build());
        
        _assertion.AssertionName = Assertion!;
        _assertion.Links = allLinks.ToList();
        return _assertion;
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
        _reporter = new AllureReporter { Name = Name! };

        _reporter.DisplayTrace = DisplayTrace;
        _reporter.SaveSessionData = SaveSessionData;
        _reporter.SaveAttachments = SaveAttachments;
        _reporter.SaveTemplate = SaveTemplate;
        _reporter.Severity = Severity;
        _reporter.Context = context;
        _reporter.EpochTestSuiteStartTime =
            new DateTimeOffset(testSuiteStartTimeUtc, new TimeSpan(0)).ToUnixTimeMilliseconds();

        _reporter.FileSystem = fileSystem ?? new FileSystem();
        return _reporter;
    }
}