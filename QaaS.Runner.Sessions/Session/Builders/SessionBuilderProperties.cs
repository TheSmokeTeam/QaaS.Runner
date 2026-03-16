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
    internal int? Stage { get; set; }

    internal int? RunUntilStage { get; set; }

    [Description("The category of the session, you can filter which categories to run using the -I flag")]
    internal string? Category { get; set; }

    [Description("Whether or not to save the session's output," +
                 " if false any data is discarded after its iterated over and the SessionData as a whole is not saved.")]
    [DefaultValue(true)]
    internal bool SaveData { get; set; } = true;

    [Range(uint.MinValue, uint.MaxValue)]
    [Description("The time in milliseconds to wait before the session starts")]
    [DefaultValue(0)]
    internal uint TimeoutBeforeSessionMs { get; set; } = 0;

    [Range(uint.MinValue, uint.MaxValue)]
    [Description("The time in milliseconds to wait after the session ends")]
    [DefaultValue(0)]
    internal uint TimeoutAfterSessionMs { get; set; } = 0;


    [UniquePropertyInEnumerable(nameof(ConsumerBuilder.Name))]
    [Description(
        "List of all consumers to build and run for this session. Consumers use protocols to receive data from" +
        " the application")]
    internal ConsumerBuilder[]? Consumers { get; set; } = [];

    [UniquePropertyInEnumerable(nameof(PublisherBuilder.Name))]
    [Description(
        "List of all publishers to build and run for this session. Publishers iterate over data and use protocols " +
        "to send it to the application")]
    internal PublisherBuilder[]? Publishers { get; set; } = [];

    [UniquePropertyInEnumerable(nameof(TransactionBuilder.Name))]
    [Description(
        "List of all transactions to run build and for this session. Transactions iterate over data and use " +
        "protocols to send it to the http applications, while saving the response data")]
    internal TransactionBuilder[]? Transactions { get; set; } = [];

    [UniquePropertyInEnumerable(nameof(ProbeBuilder.Name))]
    [Description(
        "List of all probes to build and run for this session. Probes are hook methods that do not return data, " +
        "and can be integrated inside session run")]
    internal ProbeBuilder[]? Probes { get; set; } = [];

    [UniquePropertyInEnumerable(nameof(CollectorBuilder.Name))]
    [Description(
        "List of all collectors to build and run for this session. Collectors fetch information about the " +
        "application from 3rd party apis on the sessions runtime")]
    internal CollectorBuilder[]? Collectors { get; set; } = [];

    [UniquePropertyInEnumerable(nameof(MockerCommandBuilder.Name))]
    [Description(
        "List of all mocker commands to run for this session. Mocker Commands trigger the mocker instance through " +
        "redis api to act specific actions")]
    internal MockerCommandBuilder[]? MockerCommands { get; set; } = [];

    internal StageConfig[] Stages { get; set; } = [];
}
