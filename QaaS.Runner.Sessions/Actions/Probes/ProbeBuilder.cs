using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using QaaS.Framework.Configurations.ConfigurationBindingUtils;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Sessions.ConfigurationObjects;
using QaaS.Runner.Sessions.Extensions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace QaaS.Runner.Sessions.Actions.Probes;

public class ProbeBuilder : IYamlConvertible
{
    [Required]
    [Description("The name of the probe")]
    public string? Name { get; internal set; }

    [Required]
    [Description("The name of the probe to use")]
    internal string? Probe { get; set; }

    [Description("The stage in which the Probe runs at")]
    [DefaultValue((int)OrderedActions.Probes)]
    internal int Stage { get; set; } = (int)OrderedActions.Probes;

    [Description("Names of the pre defined data sources to pass to the probe")]
    internal string[] DataSourceNames { get; set; } = [];

    [Description("Regex patterns of data sources")]
    internal string[] DataSourcePatterns { get; set; } = [];

    [Description("Implementation configuration for the probe, " +
                 "the configuration given here is loaded into the provided probe dynamically.")]
    internal IConfiguration ProbeConfiguration { get; set; } = new ConfigurationBuilder().Build();

    public void Read(IParser parser, Type expectedType, ObjectDeserializer nestedObjectDeserializer)
    {
        throw new NotSupportedException($"{nameof(Read)} doesn't support custom" +
                                        $" deserialization from Yaml for {nameof(ProbeBuilder)}");
    }

    [ExcludeFromCodeCoverage]
    public void Write(IEmitter emitter, ObjectSerializer nestedObjectSerializer)
    {
        var probeConfiguration = ProbeConfiguration
            .GetDictionaryFromConfiguration();
        nestedObjectSerializer(new
        {
            Name,
            Probe,
            Stage,
            ProbeConfiguration = probeConfiguration
        });
    }

    public ProbeBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    public ProbeBuilder AtStage(int stage)
    {
        Stage = stage;
        return this;
    }

    public ProbeBuilder HookNamed(string hookName)
    {
        Probe = hookName;
        return this;
    }

    public ProbeBuilder AddDataSourceName(string dataSourceName)
    {
        var dataSourceNamesList = DataSourceNames.ToList() ?? [];
        dataSourceNamesList.Add(dataSourceName);
        DataSourceNames = dataSourceNamesList.ToArray();
        return this;
    }

    public ProbeBuilder AddDataSourcePattern(string dataSourcePattern)
    {
        var dataSourcePatternsList = DataSourcePatterns?.ToList() ?? [];
        dataSourcePatternsList.Add(dataSourcePattern);
        DataSourcePatterns = dataSourcePatternsList.ToArray();
        return this;
    }

    public ProbeBuilder Configure(object configuration)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(configuration)));
        ProbeConfiguration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        return this;
    }

    internal Probe? Build(InternalContext context, IList<KeyValuePair<string, IProbe>> probes,
        IList<ActionFailure> actionFailures, string sessionName)
    {
        try
        {
            var probeHook = probes.FirstOrDefault(pair => pair.Key == Name!).Value
                            ?? throw new ArgumentException($"Probe {Name} of type" +
                                                           $" {Probe} was not found" +
                                                           " in provided probes.");
            context.Logger.LogDebugWithMetaData("Started building Probe of type {type}", context.GetMetaDataFromContext(), probeHook.ToString());

            return new Probe(Name!, Stage, probeHook, DataSourceNames, DataSourcePatterns, context.Logger);
        }
        catch (Exception e)
        {
            actionFailures.AppendActionFailure(e, sessionName, context.Logger, "Probe", Name!, Probe!);
        }

        return null;
    }
}