using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using QaaS.Framework.Configurations;
using QaaS.Framework.Configurations.CustomValidationAttributes;
using QaaS.Framework.Policies;
using QaaS.Framework.Protocols.ConfigurationObjects;
using QaaS.Framework.Protocols.ConfigurationObjects.Grpc;
using QaaS.Framework.Protocols.ConfigurationObjects.Http;
using QaaS.Framework.Protocols.Protocols.Factories;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Sessions.ConfigurationObjects;
using QaaS.Runner.Sessions.Extensions;
using QaaS.Runner.Sessions.RuntimeOverrides;

namespace QaaS.Runner.Sessions.Actions.Transactions.Builders;

public class TransactionBuilder
{
    [Required]
    [Description("The communication action's name which acts as a unique identifier," +
                 " used as the name of the communication action's produced input/output")]
    public string? Name { get; internal set; }

    [RequiredIfAny(nameof(DataSourcePatterns), [null])]
    [Description(
        "The name of the data sources to publish the data of" +
        " in the order their data will be published")]
    public string[]? DataSourceNames { get; internal set; }
    [RequiredIfAny(nameof(DataSourceNames), [null])]
    [Description("Patterns of the names of data sources to publish the data of off")]
    public string[]? DataSourcePatterns { get; internal set; }
    [Description("How much iterations of the publishing action to execute")]
    [DefaultValue(1)]
    [Range(1, int.MaxValue)]
    public int Iterations { get; internal set; } = 1;
    [Description("Whether to publish in loop")]
    [DefaultValue(false)]
    public bool Loop { get; internal set; }
    [Range(ulong.MinValue, ulong.MaxValue),
     Description("The time to sleep in milliseconds in between iterations"), DefaultValue(0)]
    public ulong SleepTimeMs { get; internal set; } = 0;
    [Required]
    [Description(
        "the consumption timeout in milliseconds (timeout is the time to wait for a response after sending a request)")]
    public int? TimeoutMs { get; internal set; }
    [Description("How to filter the properties of each returned sent (input) data")]
    public DataFilter InputDataFilter { get; internal set; } = new();
    [Description("How to filter the properties of each returned received (output) data")]
    public DataFilter OutputDataFilter { get; internal set; } = new();
    [Description("The stage in which the Transaction runs at")]
    [DefaultValue((int)OrderedActions.Transactions)]
    public int Stage { get; internal set; } = (int)OrderedActions.Transactions;
    [Description("List of policies to use when communicating with this action's protocol")]
    public PolicyBuilder[] Policies { get; internal set; } = [];
    [Description("The serializer to use to serialize the sent data")]
    [DefaultValue(null)]
    public SerializeConfig? InputSerialize { get; internal set; }
    [Description("The deserializer to use to deserialize the received data")]
    [DefaultValue(null)]
    public DeserializeConfig? OutputDeserialize { get; internal set; }
    [Description("Sends an http request")] internal HttpTransactorConfig? Http { get; set; }

    [Description("Invokes a Grpc Method")] internal GrpcTransactorConfig? Grpc { get; set; }
    public ITransactorConfig? Configuration
    {
        get => (ITransactorConfig?)Http ?? Grpc;
        internal set
        {
            if (value == null)
            {
                Reset();
                return;
            }

            Configure(value);
        }
    }

    /// <summary>
    /// Sets the name used for the current Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    /// <summary>
    /// Sets the stage used by the current Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder AtStage(int stage)
    {
        Stage = stage;
        return this;
    }

    /// <summary>
    /// Configures timeout on the current Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder WithTimeout(int timeoutMs)
    {
        TimeoutMs = timeoutMs;
        return this;
    }

    /// <summary>
    /// Sets the input data filter used by the transaction.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder FilterInputData(DataFilter dataFilter)
    {
        InputDataFilter = dataFilter;
        return this;
    }

    /// <summary>
    /// Sets the output data filter used by the transaction.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder FilterOutputData(DataFilter dataFilter)
    {
        OutputDataFilter = dataFilter;
        return this;
    }

    /// <summary>
    /// Sets the deserializer configuration used by the current Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder WithDeserializer(DeserializeConfig deserializeConfig)
    {
        OutputDeserialize = deserializeConfig;
        return this;
    }

    /// <summary>
    /// Sets the serializer configuration used by the current Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder WithSerializer(SerializeConfig serializeConfig)
    {
        InputSerialize = serializeConfig;
        return this;
    }

    /// <summary>
    /// Sets how many iterations the transaction should execute.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder WithIterations(int iterations)
    {
        Iterations = iterations;
        return this;
    }

    /// <summary>
    /// Adds the supplied data source to the current Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    internal TransactionBuilder AddDataSource(string dataSourceName)
    {
        var dataSourceNamesList = DataSourceNames?.ToList() ?? [];
        dataSourceNamesList.Add(dataSourceName);
        DataSourceNames = dataSourceNamesList.ToArray();
        return this;
    }

    /// <summary>
    /// Creates or adds the configured data source entry on the current Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder CreateDataSource(string dataSourceName)
    {
        return AddDataSource(dataSourceName);
    }

    /// <summary>
    /// Adds the supplied data source pattern to the current Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    internal TransactionBuilder AddDataSourcePattern(string dataSourcePattern)
    {
        var dataSourcePatternsList = DataSourcePatterns?.ToList() ?? [];
        dataSourcePatternsList.Add(dataSourcePattern);
        DataSourcePatterns = dataSourcePatternsList.ToArray();
        return this;
    }

    /// <summary>
    /// Creates or adds the configured data source pattern entry on the current Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder CreateDataSourcePattern(string dataSourcePattern)
    {
        return AddDataSourcePattern(dataSourcePattern);
    }

    /// <summary>
    /// Marks the transaction to execute continuously in loop mode.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder InLoops()
    {
        Loop = true;
        return this;
    }

    /// <summary>
    /// Sets the delay applied between transaction iterations.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder WithSleep(ulong sleepTimeMs)
    {
        SleepTimeMs = sleepTimeMs;
        return this;
    }

    /// <summary>
    /// Adds the supplied policy to the current Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    internal TransactionBuilder AddPolicy(PolicyBuilder policy)
    {
        var policies = Policies.ToList();
        policies.Add(policy);
        Policies = policies.ToArray();
        return this;
    }

    /// <summary>
    /// Creates or adds the configured policy entry on the current Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder CreatePolicy(PolicyBuilder policy)
    {
        return AddPolicy(policy);
    }

    /// <summary>
    /// Returns the configured policies currently stored on the Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public IReadOnlyList<PolicyBuilder> ReadPolicies()
    {
        return Policies;
    }

    /// <summary>
    /// Updates the configured policy at the specified index on the current Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder UpdatePolicyAt(int index, PolicyBuilder policy)
    {
        if (index < 0 || index >= Policies.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Policies[index] = policy;
        return this;
    }

    /// <summary>
    /// Removes the configured policy at the specified index from the current Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder DeletePolicyAt(int index)
    {
        if (index < 0 || index >= Policies.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Policies = Policies.Where((_, i) => i != index).ToArray();
        return this;
    }

    /// <summary>
    /// Returns the configured data sources currently stored on the Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public IReadOnlyList<string> ReadDataSources()
    {
        return DataSourceNames ?? [];
    }

    /// <summary>
    /// Updates the configured data source stored on the current Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder UpdateDataSource(string existingValue, string newValue)
    {
        if (DataSourceNames == null)
        {
            return this;
        }

        var index = Array.IndexOf(DataSourceNames, existingValue);
        if (index >= 0)
        {
            DataSourceNames[index] = newValue;
        }

        return this;
    }

    /// <summary>
    /// Removes the configured data source from the current Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder DeleteDataSource(string dataSourceName)
    {
        DataSourceNames = DataSourceNames?.Where(value => value != dataSourceName).ToArray();
        return this;
    }

    /// <summary>
    /// Returns the configured data source patterns currently stored on the Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public IReadOnlyList<string> ReadDataSourcePatterns()
    {
        return DataSourcePatterns ?? [];
    }

    /// <summary>
    /// Updates the configured data source pattern stored on the current Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder UpdateDataSourcePattern(string existingValue, string newValue)
    {
        if (DataSourcePatterns == null)
        {
            return this;
        }

        var index = Array.IndexOf(DataSourcePatterns, existingValue);
        if (index >= 0)
        {
            DataSourcePatterns[index] = newValue;
        }

        return this;
    }

    /// <summary>
    /// Removes the configured data source pattern from the current Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder DeleteDataSourcePattern(string dataSourcePattern)
    {
        DataSourcePatterns = DataSourcePatterns?.Where(value => value != dataSourcePattern).ToArray();
        return this;
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    internal TransactionBuilder AddConfiguration(ITransactorConfig config)
    {
        return Configure(config);
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    internal TransactionBuilder Create(ITransactorConfig config)
    {
        return AddConfiguration(config);
    }

    /// <summary>
    /// Updates the configuration currently stored on the Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder UpdateConfiguration(Func<ITransactorConfig, ITransactorConfig> update)
    {
        var currentConfig = Configuration ??
                            throw new InvalidOperationException("Transaction configuration is not set");
        return UpdateConfiguration(update(currentConfig));
    }

    /// <summary>
    /// Updates the configuration currently stored on the Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder UpdateConfiguration(ITransactorConfig config)
    {
        var currentConfig = Configuration ??
                            throw new InvalidOperationException("Transaction configuration is not set");
        return Configure(currentConfig.UpdateConfiguration(config));
    }

    /// <summary>
    /// Updates the configuration currently stored on the Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder UpdateConfiguration(object configuration)
    {
        var currentConfig = Configuration ??
                            throw new InvalidOperationException("Transaction configuration is not set");
        return Configure(currentConfig.UpdateConfiguration(configuration));
    }

    /// <summary>
    /// Clears the configuration currently stored on the Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder DeleteConfiguration()
    {
        return Reset();
    }

    private TransactionBuilder Reset()
    {
        Http = null;
        Grpc = null;
        return this;
    }

    /// <summary>
    /// Sets the configuration currently stored on the Runner transaction builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner transaction builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Transactions" />
    public TransactionBuilder Configure(ITransactorConfig config)
    {
        Reset();
        switch (config)
        {
            case HttpTransactorConfig httpTransactorConfig:
                Http = httpTransactorConfig;
                break;
            case GrpcTransactorConfig grpcTransactorConfig:
                Grpc = grpcTransactorConfig;
                break;
        }

        return this;
    }

    /// <summary>
    /// Builds a runtime transaction action with validated transactor configuration and policy pipeline.
    /// Failures are collected in <paramref name="actionFailures"/> and return null.
    /// </summary>
    internal Transaction? Build(InternalContext context, IList<ActionFailure> actionFailures, string sessionName)
    {
        ITransactorConfig? type = null;
        try
        {
            var allTypes = new List<ITransactorConfig?>
            {
                Http, Grpc
            };
            type = allTypes.FirstOrDefault(configuredType => configuredType != null) ??
                   throw new InvalidOperationException($"Missing supported type in transaction {Name}");
            if (allTypes.Count(config => config != null) > 1)
            {
                var conflictingConfigs = allTypes
                    .Where(config => config != null)
                    .Select(config => config!.GetType().Name)
                    .ToArray();
                throw new InvalidOperationException(
                    $"Multiple configurations provided for Transaction '{Name}': {string.Join(", ", conflictingConfigs)}. " +
                    "Only one type is allowed at a time.");
            }

            var timeout = TimeSpan.FromMilliseconds(TimeoutMs!.Value);
            var deserializerSpecificType = OutputDeserialize?.SpecificType?.GetConfiguredType();

            var overrideRequest = new TransactionOverrideRequest(Name!, type, context.Logger, timeout);
            var transactor = context.GetSessionActionOverrides()?.Transaction?.Invoke(overrideRequest)
                             ?? TransactorFactory.CreateTransactor(type, context.Logger, timeout);

            return new Transaction(Name!, transactor, Stage, InputDataFilter, OutputDataFilter,
                PolicyBuilder.BuildPolicies(Policies), Loop, Iterations, SleepTimeMs,
                InputSerialize?.Serializer, OutputDeserialize?.Deserializer, deserializerSpecificType,
                DataSourcePatterns, DataSourceNames, context.Logger);
        }
        catch (Exception e)
        {
            actionFailures.AppendActionFailure(e, sessionName, context.Logger, nameof(Transaction), Name!,
                type?.GetType().Name);
        }

        return null;
    }
}
