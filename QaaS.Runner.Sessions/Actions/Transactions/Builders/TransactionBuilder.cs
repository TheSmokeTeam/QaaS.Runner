using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
using QaaS.Runner.Infrastructure;
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
    internal string[]? DataSourceNames { get; set; }

    [RequiredIfAny(nameof(DataSourceNames), [null])]
    [Description("Patterns of the names of data sources to publish the data of off")]
    internal string[]? DataSourcePatterns { get; set; }

    [Description("How much iterations of the publishing action to execute")]
    [DefaultValue(1)]
    [Range(1, int.MaxValue)]
    internal int Iterations { get; set; } = 1;

    [Description("Whether to publish in loop")]
    [DefaultValue(false)]
    internal bool Loop { get; set; }

    [Range(ulong.MinValue, ulong.MaxValue),
     Description("The time to sleep in milliseconds in between iterations"), DefaultValue(0)]
    internal ulong SleepTimeMs { get; set; } = 0;

    [Required]
    [Description(
        "the consumption timeout in milliseconds (timeout is the time to wait for a response after sending a request)")]
    internal int? TimeoutMs { get; set; }

    [Description("How to filter the properties of each returned sent (input) data")]
    internal DataFilter InputDataFilter { get; set; } = new();

    [Description("How to filter the properties of each returned received (output) data")]
    internal DataFilter OutputDataFilter { get; set; } = new();

    [Description("The stage in which the Transaction runs at")]
    [DefaultValue((int)OrderedActions.Transactions)]
    internal int Stage { get; set; } = (int)OrderedActions.Transactions;

    [Description("List of policies to use when communicating with this action's protocol")]
    internal PolicyBuilder[] Policies { get; set; } = [];

    [Description("The serializer to use to serialize the sent data")]
    [DefaultValue(null)]
    internal SerializeConfig? InputSerialize { get; set; }

    [Description("The deserializer to use to deserialize the received data")]
    [DefaultValue(null)]
    internal DeserializeConfig? OutputDeserialize { get; set; }

    [Description("Sends an http request")] internal HttpTransactorConfig? Http { get; set; }

    [Description("Invokes a Grpc Method")] internal GrpcTransactorConfig? Grpc { get; set; }

    public TransactionBuilder Named(string name)
    {
        Name = name;
        return this;
    }

    public TransactionBuilder AtStage(int stage)
    {
        Stage = stage;
        return this;
    }

    public TransactionBuilder WithTimeout(int timeoutMs)
    {
        TimeoutMs = timeoutMs;
        return this;
    }

    public TransactionBuilder FilterInputData(DataFilter dataFilter)
    {
        InputDataFilter = dataFilter;
        return this;
    }

    public TransactionBuilder FilterOutputData(DataFilter dataFilter)
    {
        OutputDataFilter = dataFilter;
        return this;
    }

    public TransactionBuilder WithDeserializer(DeserializeConfig deserializeConfig)
    {
        OutputDeserialize = deserializeConfig;
        return this;
    }

    public TransactionBuilder WithSerializer(SerializeConfig serializeConfig)
    {
        InputSerialize = serializeConfig;
        return this;
    }

    public TransactionBuilder WithIterations(int iterations)
    {
        Iterations = iterations;
        return this;
    }

    public TransactionBuilder AddDataSource(string dataSourceName)
    {
        var dataSourceNamesList = DataSourceNames?.ToList() ?? [];
        dataSourceNamesList.Add(dataSourceName);
        DataSourceNames = dataSourceNamesList.ToArray();
        return this;
    }

    public TransactionBuilder CreateDataSource(string dataSourceName)
    {
        return AddDataSource(dataSourceName);
    }

    public TransactionBuilder AddDataSourcePattern(string dataSourcePattern)
    {
        var dataSourcePatternsList = DataSourcePatterns?.ToList() ?? [];
        dataSourcePatternsList.Add(dataSourcePattern);
        DataSourcePatterns = dataSourcePatternsList.ToArray();
        return this;
    }

    public TransactionBuilder CreateDataSourcePattern(string dataSourcePattern)
    {
        return AddDataSourcePattern(dataSourcePattern);
    }

    public TransactionBuilder InLoops()
    {
        Loop = true;
        return this;
    }

    public TransactionBuilder WithSleep(ulong sleepTimeMs)
    {
        SleepTimeMs = sleepTimeMs;
        return this;
    }

    public TransactionBuilder AddPolicy(PolicyBuilder policy)
    {
        var policies = Policies.ToList();
        policies.Add(policy);
        Policies = policies.ToArray();
        return this;
    }

    public TransactionBuilder CreatePolicy(PolicyBuilder policy)
    {
        return AddPolicy(policy);
    }

    public IReadOnlyList<PolicyBuilder> ReadPolicies()
    {
        return Policies;
    }

    public TransactionBuilder UpdatePolicyAt(int index, PolicyBuilder policy)
    {
        if (index < 0 || index >= Policies.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Policies[index] = policy;
        return this;
    }

    public TransactionBuilder DeletePolicyAt(int index)
    {
        if (index < 0 || index >= Policies.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Policies = Policies.Where((_, i) => i != index).ToArray();
        return this;
    }

    public IReadOnlyList<string> ReadDataSources()
    {
        return DataSourceNames ?? [];
    }

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

    public TransactionBuilder DeleteDataSource(string dataSourceName)
    {
        DataSourceNames = DataSourceNames?.Where(value => value != dataSourceName).ToArray();
        return this;
    }

    public IReadOnlyList<string> ReadDataSourcePatterns()
    {
        return DataSourcePatterns ?? [];
    }

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

    public TransactionBuilder DeleteDataSourcePattern(string dataSourcePattern)
    {
        DataSourcePatterns = DataSourcePatterns?.Where(value => value != dataSourcePattern).ToArray();
        return this;
    }

    /// <summary>
    /// Compatibility alias for <see cref="Configure" /> that matches the configuration CRUD pattern used by other builders.
    /// </summary>
    public TransactionBuilder CreateConfiguration(ITransactorConfig config)
    {
        return Configure(config);
    }

    /// <summary>
    /// Returns the currently configured transactor source, if any.
    /// </summary>
    public ITransactorConfig? ReadConfiguration()
    {
        return (ITransactorConfig?)Http ?? Grpc;
    }

    /// <summary>
    /// Applies a computed partial update to the current transaction configuration while preserving omitted fields.
    /// </summary>
    public TransactionBuilder UpdateConfiguration(Func<ITransactorConfig, ITransactorConfig> update)
    {
        var currentConfig = ReadConfiguration() ??
                            throw new InvalidOperationException("Transaction configuration is not set");
        return UpdateConfiguration(update(currentConfig));
    }

    /// <summary>
    /// Updates the transaction configuration by merging same-type values and replacing the current type when needed.
    /// </summary>
    public TransactionBuilder UpdateConfiguration(ITransactorConfig config)
    {
        var currentConfig = ReadConfiguration() ??
                            throw new InvalidOperationException("Transaction configuration is not set");
        return Configure(currentConfig.UpdateConfiguration(config));
    }

    /// <summary>
    /// Clears the configured transactor source.
    /// </summary>
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
    /// Replaces the current transactor source with the provided configuration type.
    /// </summary>
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
