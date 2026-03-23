using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using QaaS.Framework.Configurations.CustomValidationAttributes;
using QaaS.Runner.Sessions.Actions.Collectors;
using QaaS.Runner.Sessions.Actions.Consumers.Builders;
using QaaS.Runner.Sessions.Actions.MockerCommands;
using QaaS.Runner.Sessions.Actions.Probes;
using QaaS.Runner.Sessions.Actions.Publishers.Builders;
using QaaS.Runner.Sessions.Actions.Transactions.Builders;

[assembly: InternalsVisibleTo("QaaS.Runner")]
[assembly: InternalsVisibleTo("QaaS.Runner.Tests")]

namespace QaaS.Runner.Sessions.Session.Builders;

public partial class SessionBuilder
{
    [Required]
    [RegularExpression(
        @"^[^\\/]*$", ErrorMessage = "The field Name cannot contain / or \\.", MatchTimeoutInMilliseconds = 5000)]
    [Description("The name of the session, used to uniquely identify it and its output")]
    public string? Name { get; internal set; }

    [Description(
        "The stage of the session. Sessions with the same stage runs together. stage defaultly gets the index of the session in session list ")]
    public int? Stage { get; internal set; }
    [Description(
        "Optional stage number that decides when the runner waits for this session to complete. " +
        "If omitted, the session becomes visible only after its own stage completes. " +
        "If set, the runner defers waiting until the configured future stage is reached.")]
    public int? RunUntilStage { get; internal set; }
    [Description("The category of the session, you can filter which categories to run using the -I flag")]
    public string? Category { get; internal set; }
    [Description("Whether or not to save the session's output," +
                 " if false any data is discarded after its iterated over and the SessionData as a whole is not saved.")]
    [DefaultValue(true)]
    public bool SaveData { get; internal set; } = true;
    [Range(uint.MinValue, uint.MaxValue)]
    [Description("The time in milliseconds to wait before the session starts")]
    [DefaultValue(0)]
    public uint TimeoutBeforeSessionMs { get; internal set; } = 0;
    [Range(uint.MinValue, uint.MaxValue)]
    [Description("The time in milliseconds to wait after the session ends")]
    [DefaultValue(0)]
    public uint TimeoutAfterSessionMs { get; internal set; } = 0;
    [UniquePropertyInEnumerable(nameof(ConsumerBuilder.Name))]
    [Description(
        "List of all consumers to build and run for this session. Consumers use protocols to receive data from" +
        " the application")]
    public ConsumerBuilder[]? Consumers { get; internal set; } = [];
    [UniquePropertyInEnumerable(nameof(PublisherBuilder.Name))]
    [Description(
        "List of all publishers to build and run for this session. Publishers iterate over data and use protocols " +
        "to send it to the application")]
    public PublisherBuilder[]? Publishers { get; internal set; } = [];
    [UniquePropertyInEnumerable(nameof(TransactionBuilder.Name))]
    [Description(
        "List of all transactions to run build and for this session. Transactions iterate over data and use " +
        "protocols to send it to the http applications, while saving the response data")]
    public TransactionBuilder[]? Transactions { get; internal set; } = [];
    [UniquePropertyInEnumerable(nameof(ProbeBuilder.Name))]
    [Description(
        "List of all probes to build and run for this session. Probes are hook methods that do not return data, " +
        "and can be integrated inside session run")]
    public ProbeBuilder[]? Probes { get; internal set; } = [];
    [UniquePropertyInEnumerable(nameof(CollectorBuilder.Name))]
    [Description(
        "List of all collectors to build and run for this session. Collectors fetch information about the " +
        "application from 3rd party apis on the sessions runtime")]
    public CollectorBuilder[]? Collectors { get; internal set; } = [];
    [UniquePropertyInEnumerable(nameof(MockerCommandBuilder.Name))]
    [Description(
        "List of all mocker commands to run for this session. Mocker Commands trigger the mocker instance through " +
        "redis api to act specific actions")]
    public MockerCommandBuilder[]? MockerCommands { get; internal set; } = [];
    [Description(
        "Optional per-stage configuration for the session's internal action stages. " +
        "Use this to override timing around a specific stage number without changing the action order.")]
    public StageConfig[] Stages { get; internal set; } = [];
}
