using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using QaaS.Framework.Configurations;
using QaaS.Framework.Configurations.ConfigurationBindingUtils;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Infrastructure;
using QaaS.Runner.Sessions.ConfigurationObjects;
using QaaS.Runner.Sessions.Extensions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace QaaS.Runner.Sessions.Actions.Probes;

/// <summary>
/// Fluent builder for probe configuration and runtime probe action creation.
/// </summary>
public class ProbeBuilder : IYamlConvertible
{
    [Required]
    [Description("The name of the probe")]
    public string? Name { get; internal set; }

    [Required]
    [Description("The name of the probe to use")]
    public string? Probe { get; internal set; }
    [Description("The stage in which the Probe runs at")]
    [DefaultValue((int)OrderedActions.Probes)]
    public int Stage { get; internal set; } = (int)OrderedActions.Probes;
    [Description("Names of the pre defined data sources to pass to the probe")]
    public string[] DataSourceNames { get; internal set; } = [];
    [Description("Regex patterns of data sources")]
    public string[] DataSourcePatterns { get; internal set; } = [];
    [Description("Implementation configuration for the probe, " +
                 "the configuration given here is loaded into the provided probe dynamically.")]
    public IConfiguration ProbeConfiguration { get; internal set; } = new ConfigurationBuilder().Build();
    public IConfiguration Configuration
    {
        get => ProbeConfiguration;
        internal set => ProbeConfiguration = value ?? new ConfigurationBuilder().Build();
    }
    /// <summary>
    /// Reads the serialized configuration for the current Runner probe builder instance.
    /// </summary>
    /// <remarks>
    /// This method participates in the YAML serialization surface that backs configuration-as-code support.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Probes" />
    public void Read(IParser parser, Type expectedType, ObjectDeserializer nestedObjectDeserializer)
    {
        throw new NotSupportedException($"{nameof(Read)} doesn't support custom" +
                                        $" deserialization from Yaml for {nameof(ProbeBuilder)}");
    }

    /// <summary>
    /// Writes the current Runner probe builder configuration to the configured serializer output.
    /// </summary>
    /// <remarks>
    /// This method participates in the YAML serialization surface that backs configuration-as-code support.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Probes" />
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

    /// <summary>
    /// Sets the name used for the current Runner probe builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner probe builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Probes" />
    public ProbeBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    /// <summary>
    /// Sets the stage used by the current Runner probe builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner probe builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Probes" />
    public ProbeBuilder AtStage(int stage)
    {
        Stage = stage;
        return this;
    }

    /// <summary>
    /// Sets the hook implementation name used by the current Runner probe builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner probe builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Probes" />
    public ProbeBuilder HookNamed(string hookName)
    {
        Probe = hookName;
        return this;
    }

    /// <summary>
    /// Adds the supplied data source name to the current Runner probe builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner probe builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Probes" />
    public ProbeBuilder AddDataSourceName(string dataSourceName)
    {
        var dataSourceNamesList = DataSourceNames?.ToList() ?? [];
        dataSourceNamesList.Add(dataSourceName);
        DataSourceNames = dataSourceNamesList.ToArray();
        return this;
    }

    /// <summary>
    /// Removes the configured data source name from the current Runner probe builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner probe builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Probes" />
    public ProbeBuilder RemoveDataSourceName(string dataSourceName)
    {
        DataSourceNames = (DataSourceNames ?? []).Where(value => value != dataSourceName).ToArray();
        return this;
    }

    /// <summary>
    /// Removes the configured data source name at the specified index from the current Runner probe builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner probe builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Probes" />
    public ProbeBuilder RemoveDataSourceNameAt(int index)
    {
        DataSourceNames = RemoveAt(DataSourceNames, index);
        return this;
    }

    /// <summary>
    /// Adds the supplied data source pattern to the current Runner probe builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner probe builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Probes" />
    public ProbeBuilder AddDataSourcePattern(string dataSourcePattern)
    {
        var dataSourcePatternsList = DataSourcePatterns?.ToList() ?? [];
        dataSourcePatternsList.Add(dataSourcePattern);
        DataSourcePatterns = dataSourcePatternsList.ToArray();
        return this;
    }

    /// <summary>
    /// Removes the configured data source pattern from the current Runner probe builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner probe builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Probes" />
    public ProbeBuilder RemoveDataSourcePattern(string dataSourcePattern)
    {
        DataSourcePatterns = (DataSourcePatterns ?? []).Where(value => value != dataSourcePattern).ToArray();
        return this;
    }

    /// <summary>
    /// Removes the configured data source pattern at the specified index from the current Runner probe builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner probe builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Probes" />
    public ProbeBuilder RemoveDataSourcePatternAt(int index)
    {
        DataSourcePatterns = RemoveAt(DataSourcePatterns, index);
        return this;
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner probe builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner probe builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Probes" />
    public ProbeBuilder Configure(object configuration)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(configuration)));
        ProbeConfiguration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        return this;
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner probe builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner probe builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Probes" />
    public ProbeBuilder UpdateConfiguration(object configuration)
    {
        ProbeConfiguration = (ProbeConfiguration ?? new ConfigurationBuilder().Build())
            .UpdateConfiguration(configuration);
        return this;
    }

    /// <summary>
    /// Clears the configuration currently stored on the Runner probe builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner probe builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Probes" />
    public ProbeBuilder RemoveConfiguration()
    {
        ProbeConfiguration = new ConfigurationBuilder().Build();
        return this;
    }

    private static T[] RemoveAt<T>(T[]? values, int index)
    {
        if (values == null)
        {
            return [];
        }

        if (index < 0 || index >= values.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return values.Where((_, i) => i != index).ToArray();
    }

    /// <summary>
    /// Resolves the configured probe hook and constructs a runtime <see cref="Probe"/> action.
    /// Failures are captured into <paramref name="actionFailures"/> so session build can continue.
    /// </summary>
    internal Probe? Build(InternalContext context, IList<KeyValuePair<string, IProbe>> probes,
        IList<ActionFailure> actionFailures, string sessionName)
    {
        var probeName = Name ?? "<missing-probe-name>";
        var probeType = Probe ?? "<missing-probe-type>";

        try
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                throw new ValidationException("Probe Name is required.");
            }

            var scopedHookName = BuildScopedHookName(sessionName, Name);
            var probeHook = probes.FirstOrDefault(pair => pair.Key == scopedHookName).Value
                            ?? throw new ArgumentException($"Probe {Name} of type" +
                                                           $" {Probe} in session {sessionName} was not found" +
                                                           " in provided probes.");
            var probeTypeName = probeHook.GetType().Name;
            context.Logger.LogDebugWithMetaData("Started building Probe of type {type}",
                context.GetMetaDataOrDefault(), new object?[] { probeTypeName });

            return new Probe(Name!, sessionName, Stage, probeHook, DataSourceNames, DataSourcePatterns, context.Logger);
        }
        catch (Exception e)
        {
            actionFailures.AppendActionFailure(e, sessionName, context.Logger, "Probe", probeName, probeType);
        }

        return null;
    }

    internal static string BuildScopedHookName(string sessionName, string probeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(probeName);
        return $"{sessionName.Length}:{sessionName}{probeName}";
    }

    internal static (string SessionName, string ProbeName) ParseScopedHookName(string scopedHookName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopedHookName);

        var delimiterIndex = scopedHookName.IndexOf(':');
        if (delimiterIndex <= 0 ||
            !int.TryParse(scopedHookName[..delimiterIndex], out var sessionNameLength) ||
            sessionNameLength < 0)
        {
            throw new FormatException($"Probe scoped hook name '{scopedHookName}' is not in the expected format.");
        }

        var sessionNameStartIndex = delimiterIndex + 1;
        if (scopedHookName.Length < sessionNameStartIndex + sessionNameLength)
        {
            throw new FormatException($"Probe scoped hook name '{scopedHookName}' is truncated.");
        }

        var sessionName = scopedHookName.Substring(sessionNameStartIndex, sessionNameLength);
        var probeName = scopedHookName[(sessionNameStartIndex + sessionNameLength)..];
        if (string.IsNullOrWhiteSpace(probeName))
        {
            throw new FormatException($"Probe scoped hook name '{scopedHookName}' is missing the probe name.");
        }

        return (sessionName, probeName);
    }
}
