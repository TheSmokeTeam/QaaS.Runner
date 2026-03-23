using System.Collections;
using System.Collections.Immutable;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.CompilerServices;
using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QaaS.Framework.Configurations;
using QaaS.Framework.Configurations.CustomAttributes;
using QaaS.Framework.Configurations.CustomExceptions;
using QaaS.Framework.Configurations.CustomValidationAttributes;
using QaaS.Framework.Executions;
using QaaS.Framework.Providers;
using QaaS.Framework.Providers.Modules;
using QaaS.Framework.Providers.ObjectCreation;
using QaaS.Framework.Providers.Providers;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Hooks.Generator;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Runner.Assertions;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.Extensions;
using QaaS.Runner.Infrastructure;
using QaaS.Runner.Sessions.Actions.Probes;
using QaaS.Runner.Logics;
using QaaS.Runner.Sessions.Session;
using QaaS.Runner.Sessions.Session.Builders;
using QaaS.Runner.Storage;
using QaaS.Runner.Storage.ConfigurationObjects;
using ILogger = Microsoft.Extensions.Logging.ILogger;

[assembly: InternalsVisibleTo("QaaS.Runner.Tests")]
[assembly: InternalsVisibleTo("QaaS.Runner.Sessions.Tests")]

namespace QaaS.Runner;

/// <summary>
/// Builds <see cref="Execution" /> instance
/// </summary>
[JsonSchema]
public class ExecutionBuilder() : BaseExecutionBuilder<InternalContext, ExecutionData>
{
    /// <summary>
    /// List of all sessions to run.
    /// Sessions contain the actions performed against the tested system and its underlying infrastructure
    /// in order to receive response data from the tested system to assert on.
    /// </summary>
    [UniquePropertyInEnumerable(nameof(SessionBuilder.Name))]
    [UniquePropertyInEnumerableProperties("Name",
        "Can't have the same name across multiple publishers/transactions since they all produce `SessionData.Input`.",
        "Publishers", "Transactions")]
    [UniquePropertyInEnumerableProperties("Name",
        "Can't have the same name across multiple consumers/transactions/collectors since they all produce `SessionData.Output`.",
        "Consumers", "Transactions", "Collectors")]
    [Description("List of all sessions to run. Sessions contain the actions" +
                 " performed against the tested system and its underlying infrastructure in order to receive" +
                 " response data from the tested system to assert on.")]
    public SessionBuilder[]? Sessions { get; internal set; } = [];
    /// <summary>
    /// External storages qaas inner objects can be stored in or retrieved from when
    /// using the `qaas act` (to create and store) or `qaas assert` (to retrieve and use) commands
    /// </summary>
    [Description(
        "External storages qaas inner objects can be stored in or retrieved from when using " +
        "the `qaas act` (to create and store) or `qaas assert` (to retrieve and use) commands")]

    public StorageBuilder[]? Storages { get; internal set; } = [];
    /// <summary>
    /// The list of assertions performed on the sessions' results in order to decide the test's status,
    /// each assertion produces a different test result.
    /// </summary>
    [UniquePropertyInEnumerable(nameof(AssertionBuilder.Name))]
    [Description(
        "The list of assertions performed on the sessions' results in order to decide the test's status," +
        " each assertion produces a different test result.")]
    public AssertionBuilder[]? Assertions { get; internal set; } = [];
    /// <summary>
    /// The links generated on test results, used to view observability data outputted by the tested application.
    /// These links are generated per test result to be relevant specifically to that test and the time it ran at
    /// </summary>
    [Description(
        "The links generated on test results, used to view observability data outputted by the tested application. " +
        "These links are generated per test result to be relevant specifically to that test and the time it ran at")]
    public LinkBuilder[]? Links { get; internal set; } = [];
    /// <summary>
    /// The metadata for the tests' run
    /// </summary>
    [Description("The metadata for the tests' run")]
    public MetaDataConfig? MetaData { get; internal set; }
    private ExecutionType Type { get; set; }

    private bool LoadedContext { get; }

    private readonly Autofac.IContainer _container = new ContainerBuilder().Build();
    private ILifetimeScope? _buildScope;

    private readonly List<ValidationResult> _validationResults = [];

    private readonly IList<string>? _sessionNamesToRun;

    private readonly IList<string>? _assertionNamesToRun;

    private readonly IList<string>? _sessionCategoriesToRun;

    private readonly IList<string>? _assertionCategoriesToRun;

    private ILogger _configuredLogger = default!;
    private string? _configuredCaseName;
    private string? _configuredExecutionId;
    private Dictionary<string, object?> _globalDict = new();
    private readonly IConfiguration? _templateSourceConfiguration;
    private const BindingFlags ValidationBindingFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    internal ExecutionBuilder(InternalContext context, ExecutionType executionType, IList<string>? sessionNamesToRun,
        IList<string>? sessionCategoriesToRun, IList<string>? assertionNamesToRun,
        IList<string>? assertionCategoriesToRun) : this()
    {
        LoadedContext = true;
        Type = executionType;

        Context = context;
        _templateSourceConfiguration = context.RootConfiguration;
        var blankRunBuilderFromContext = Bind.BindFromContext<ExecutionBuilder>(Context, _validationResults,
            new BinderOptions() { BindNonPublicProperties = true });

        DataSources = blankRunBuilderFromContext.DataSources;
        Storages = blankRunBuilderFromContext.Storages;
        Assertions = blankRunBuilderFromContext.Assertions;
        Sessions = blankRunBuilderFromContext.Sessions;
        Links = blankRunBuilderFromContext.Links;
        MetaData = blankRunBuilderFromContext.MetaData;

        _sessionNamesToRun = sessionNamesToRun != null && !sessionNamesToRun.Any() ? null : sessionNamesToRun;
        _sessionCategoriesToRun = sessionCategoriesToRun != null && !sessionCategoriesToRun.Any()
            ? null
            : sessionCategoriesToRun;
        _assertionNamesToRun = assertionNamesToRun != null && !assertionNamesToRun.Any() ? null : assertionNamesToRun;
        _assertionCategoriesToRun = assertionCategoriesToRun != null && !assertionCategoriesToRun.Any()
            ? null
            : assertionCategoriesToRun;
    }

    /// <inheritdoc />
    protected override IEnumerable<DataSource> BuildDataSources()
    {
        return BuildDataSources(_buildScope ?? throw new InvalidOperationException(
            "ExecutionBuilder scope is not initialized."));
    }

    private IEnumerable<DataSource> BuildDataSources(ILifetimeScope scope)
    {
        var configuredDataSources = DataSources ?? [];
        var dataSources = configuredDataSources.Select(dataSourceBuilder => dataSourceBuilder.Register()).ToImmutableList();
        var resolvedGenerators = scope.Resolve<IList<KeyValuePair<string, IGenerator>>>();
        var resolvedDataSources = configuredDataSources.Select(dataSourceBuilder =>
        {
            var configuredGenerator = dataSourceBuilder.Generator;

            // Runner scopes generator hook instances by data source name so each source can keep
            // its own generator configuration, even when multiple sources use the same hook type.
            dataSourceBuilder.Generator = dataSourceBuilder.Name;

            try
            {
                return dataSourceBuilder.Build(Context, dataSources, resolvedGenerators);
            }
            finally
            {
                dataSourceBuilder.Generator = configuredGenerator;
            }
        });
        return resolvedDataSources;
    }

    private IEnumerable<ISession> BuildSessions(ILifetimeScope scope)
    {
        // Assigning session stage default as the index in Sessions list
        Sessions = Sessions is null ? [] : Sessions.Select((session, index) =>
        {
            session.Stage ??= index;
            return session;
        }).ToArray();

        // Build sessions
        var sessions = Sessions.Select(session =>
        {
            // Resolve hooks
            var hooks = session.Probes != null
                ? scope.Resolve<IList<KeyValuePair<string, IProbe>>>()
                : new List<KeyValuePair<string, IProbe>>();

            return session.Build(Context, hooks);
        }).ToList();

        return sessions;
    }

    private IEnumerable<Assertion> BuildAssertions(ILifetimeScope scope)
    {
        if (Assertions is null) return [];
        var assertions = Assertions.Select(assertion =>
            assertion.Build(scope.Resolve<IList<KeyValuePair<string, IAssertion>>>(), Links));
        return assertions;
    }

    private IEnumerable<IReporter> BuildReports()
    {
        if (Assertions is null) return [];
        var resolvedReports = Assertions.SelectMany(assertionReport =>
            assertionReport.BuildReporters(Context, DateTime.UtcNow));
        return resolvedReports;
    }

    private IEnumerable<IStorage> BuildStorages()
    {
        if (Storages is null) return [];
        var resolvedStorages = Storages.Select(storage =>
            storage.Build(Context));
        return resolvedStorages;
    }

    /// <summary>
    /// Replaces the global dictionary stored on the runner execution context.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder WithGlobalDict(Dictionary<string, object?> globalDict)
    {
        _globalDict = globalDict;
        return this;
    }

    /// <summary>
    /// Adds the supplied session to the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder AddSession(SessionBuilder sessionBuilder)
    {
        Sessions = Sessions is null ? [sessionBuilder] : Sessions.Append(sessionBuilder).ToArray();
        return this;
    }

    /// <summary>
    /// Creates or adds the configured session entry on the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder CreateSession(SessionBuilder sessionBuilder)
    {
        return AddSession(sessionBuilder);
    }

    /// <summary>
    /// Returns the configured sessions currently stored on the Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public IReadOnlyList<SessionBuilder> ReadSessions()
    {
        return Sessions ?? [];
    }

    /// <summary>
    /// Updates the configured session stored on the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder UpdateSession(string sessionName, SessionBuilder sessionBuilder)
    {
        Sessions = UpdateByName(Sessions, sessionName, sessionBuilder, session => session.Name);
        return this;
    }

    /// <summary>
    /// Removes the configured session from the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder DeleteSession(string sessionName)
    {
        Sessions = DeleteByName(Sessions, sessionName, session => session.Name);
        return this;
    }

    /// <summary>
    /// Adds the supplied assertion to the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder AddAssertion(AssertionBuilder assertionBuilder)
    {
        Assertions = Assertions is null ? [assertionBuilder] : Assertions.Append(assertionBuilder).ToArray();
        return this;
    }

    /// <summary>
    /// Creates or adds the configured assertion entry on the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder CreateAssertion(AssertionBuilder assertionBuilder)
    {
        return AddAssertion(assertionBuilder);
    }

    /// <summary>
    /// Returns the configured assertions currently stored on the Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public IReadOnlyList<AssertionBuilder> ReadAssertions()
    {
        return Assertions ?? [];
    }

    /// <summary>
    /// Updates the configured assertion stored on the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder UpdateAssertion(string assertionName, AssertionBuilder assertionBuilder)
    {
        Assertions = UpdateByName(Assertions, assertionName, assertionBuilder, assertion => assertion.Name);
        return this;
    }

    /// <summary>
    /// Removes the configured assertion from the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder DeleteAssertion(string assertionName)
    {
        Assertions = DeleteByName(Assertions, assertionName, assertion => assertion.Name);
        return this;
    }

    /// <summary>
    /// Adds the supplied storage to the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder AddStorage(StorageBuilder storageBuilder)
    {
        Storages = Storages is null ? [storageBuilder] : Storages.Append(storageBuilder).ToArray();
        return this;
    }

    /// <summary>
    /// Creates or adds the configured storage entry on the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder CreateStorage(StorageBuilder storageBuilder)
    {
        return AddStorage(storageBuilder);
    }

    /// <summary>
    /// Returns the configured storages currently stored on the Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public IReadOnlyList<StorageBuilder> ReadStorages()
    {
        return Storages ?? [];
    }

    /// <summary>
    /// Updates the configured storage at the specified index on the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder UpdateStorageAt(int index, StorageBuilder storageBuilder)
    {
        Storages = UpdateAt(Storages, index, storageBuilder);
        return this;
    }

    /// <summary>
    /// Removes the configured storage at the specified index from the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder DeleteStorageAt(int index)
    {
        Storages = DeleteAt(Storages, index);
        return this;
    }

    /// <summary>
    /// Adds the supplied data source to the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder AddDataSource(DataSourceBuilder dataSourceBuilder)
    {
        DataSources = DataSources is null ? [dataSourceBuilder] : DataSources.Append(dataSourceBuilder).ToArray();
        return this;
    }

    /// <summary>
    /// Creates or adds the configured data source entry on the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder CreateDataSource(DataSourceBuilder dataSourceBuilder)
    {
        return AddDataSource(dataSourceBuilder);
    }

    /// <summary>
    /// Returns the configured data sources currently stored on the Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public IReadOnlyList<DataSourceBuilder> ReadDataSources()
    {
        return DataSources ?? [];
    }

    /// <summary>
    /// Updates the configured data source stored on the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder UpdateDataSource(string dataSourceName, DataSourceBuilder dataSourceBuilder)
    {
        DataSources = UpdateByName(DataSources, dataSourceName, dataSourceBuilder, source => source.Name);
        return this;
    }

    /// <summary>
    /// Removes the configured data source from the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder DeleteDataSource(string dataSourceName)
    {
        DataSources = DeleteByName(DataSources, dataSourceName, source => source.Name);
        return this;
    }

    /// <summary>
    /// Adds the supplied link to the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder AddLink(LinkBuilder linkBuilder)
    {
        Links = Links is null ? [linkBuilder] : Links.Append(linkBuilder).ToArray();
        return this;
    }

    /// <summary>
    /// Creates or adds the configured link entry on the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder CreateLink(LinkBuilder linkBuilder)
    {
        return AddLink(linkBuilder);
    }

    /// <summary>
    /// Returns the configured links currently stored on the Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public IReadOnlyList<LinkBuilder> ReadLinks()
    {
        return Links ?? [];
    }

    /// <summary>
    /// Returns the metadata currently stored on the Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. Use it to inspect the current configured state without rebuilding the surrounding collection or runtime object graph.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public MetaDataConfig? ReadMetaData()
    {
        return MetaData;
    }

    /// <summary>
    /// Updates the configured link at the specified index on the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder UpdateLinkAt(int index, LinkBuilder linkBuilder)
    {
        Links = UpdateAt(Links, index, linkBuilder);
        return this;
    }

    /// <summary>
    /// Removes the configured link at the specified index from the current Runner execution builder instance.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder DeleteLinkAt(int index)
    {
        Links = DeleteAt(Links, index);
        return this;
    }

    /// <summary>
    /// Sets the execution type used when the runner execution is built.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder ExecutionType(ExecutionType executionType)
    {
        Type = executionType;
        return this;
    }

    internal ExecutionBuilder WithLogger(ILogger logger)
    {
        _configuredLogger = logger;
        return this;
    }

    /// <summary>
    /// Sets the case file applied by the context builder.
    /// </summary>
    /// <remarks>
    /// Case files are used as the final scenario-specific overlay that shapes the runtime configuration for a specific execution.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder SetCase(string caseName)
    {
        _configuredCaseName = caseName;
        return this;
    }

    /// <summary>
    /// Sets the execution identifier stored on the built context.
    /// </summary>
    /// <remarks>
    /// The execution identifier flows into the built context and can later be used by logging, reports, and storage integrations.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder SetExecutionId(string executionId)
    {
        _configuredExecutionId = executionId;
        return this;
    }

    /// <summary>
    /// Sets the metadata configuration stored on the execution.
    /// </summary>
    /// <remarks>
    /// Use this method when working with the documented Runner execution builder API surface in code. The change is stored on the current builder instance and is consumed by later build, validation, or execution steps.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public ExecutionBuilder WithMetadata(MetaDataConfig metaDataConfig)
    {
        MetaData = metaDataConfig;
        return this;
    }

    /// <summary>
    ///     Loads the <see cref="ExecutionBuilder" /> scope with all context's dependencies
    /// </summary>
    private ILifetimeScope LoadContextScopeDependencies()
    {
        return _container.BeginLifetimeScope(containerBuilder =>
        {
            // Loads context into scope
            containerBuilder.RegisterInstance(Context).As<InternalContext>().SingleInstance();
            containerBuilder.RegisterInstance(Context).As<Context>().SingleInstance();
            containerBuilder.RegisterInstance(new ByNameObjectCreator(Context.Logger)).As<IByNameObjectCreator>();
            ValidateProbeDefinitions();

            containerBuilder.Register<IComponentContext, IEnumerable<HookData<IAssertion>>>(_ =>
                (Assertions ?? []).Select(assertion => new HookData<IAssertion>
                {
                    Type = assertion.Assertion!,
                    Configuration = assertion.AssertionConfiguration,
                    Name = assertion.Name!
                })
            ).InstancePerLifetimeScope(); // Loads all IAssertion hooks
            containerBuilder.Register<IComponentContext, IEnumerable<HookData<IGenerator>>>(_ =>
                (DataSources ?? []).Select(dataSourceConfig => new HookData<IGenerator>
                {
                    Type = dataSourceConfig.Generator!,
                    Configuration = dataSourceConfig.GeneratorConfiguration,
                    Name = dataSourceConfig.Name!
                })
            ).InstancePerLifetimeScope(); // Loads all IGenerator hooks
            containerBuilder
                .Register<IComponentContext, IEnumerable<HookData<IProbe>>>(_ =>
                    BuildProbeHookData())
                .InstancePerLifetimeScope(); // Loads all IProbe hooks
            containerBuilder.RegisterModule(
                new HooksLoaderModule<IAssertion>(_validationResults)); // Loads all IAssertion hooks
            containerBuilder.RegisterModule(
                new HooksLoaderModule<IGenerator>(_validationResults)); // Loads all IGenerator hooks
            containerBuilder.Register<IComponentContext, IList<KeyValuePair<string, IProbe>>>(scope =>
            {
                var objectCreator = scope.Resolve<IByNameObjectCreator>();
                return LoadProbeHooks(
                    Context,
                    scope.Resolve<IEnumerable<HookData<IProbe>>>(),
                    new HookProvider<IProbe>(Context, objectCreator),
                    objectCreator,
                    _validationResults);
            }).InstancePerLifetimeScope();

            // loads logics
            containerBuilder.RegisterType<DataSourceLogic>().As<DataSourceLogic>();
            containerBuilder.RegisterType<SessionLogic>().As<SessionLogic>();
            containerBuilder.RegisterType<StorageLogic>().As<StorageLogic>();
            containerBuilder.RegisterType<AssertionLogic>().As<AssertionLogic>();
            containerBuilder.RegisterType<ReportLogic>().As<ReportLogic>();
            containerBuilder.RegisterType<TemplateLogic>().As<TemplateLogic>();
        });
    }

    private IEnumerable<HookData<IProbe>> BuildProbeHookData()
    {
        foreach (var sessionBuilder in Sessions ?? [])
        {
            foreach (var probeBuilder in sessionBuilder.Probes ?? [])
            {
                if (string.IsNullOrWhiteSpace(sessionBuilder.Name) ||
                    string.IsNullOrWhiteSpace(probeBuilder.Name) ||
                    string.IsNullOrWhiteSpace(probeBuilder.Probe))
                    continue;

                yield return new HookData<IProbe>
                {
                    Type = probeBuilder.Probe,
                    Configuration = probeBuilder.ProbeConfiguration,
                    Name = ProbeBuilder.BuildScopedHookName(sessionBuilder.Name, probeBuilder.Name)
                };
            }
        }
    }

    /// <summary>
    /// Loads probe hooks while resolving each configured probe type only once.
    /// Multiple sessions may use the same probe implementation with different scoped names and
    /// configurations, so additional instances are created directly from the resolved type instead
    /// of asking the generic hook provider to rediscover the same type and log it again.
    /// </summary>
    internal static IList<KeyValuePair<string, IProbe>> LoadProbeHooks(
        Context context,
        IEnumerable<HookData<IProbe>> probeHookData,
        IHookProvider<IProbe> hookProvider,
        IByNameObjectCreator objectCreator,
        List<ValidationResult> validationResults)
    {
        context.Logger.LogDebug("Starting loading and validation of all hooks of type {HookType}",
            typeof(IProbe).Name);

        var resolvedProbeTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
        var loadedHooks = new List<KeyValuePair<string, IProbe>>();

        foreach (var hookData in probeHookData)
        {
            IProbe hook;

            try
            {
                if (!resolvedProbeTypes.TryGetValue(hookData.Type, out var resolvedProbeType))
                {
                    hook = hookProvider.GetSupportedInstanceByName(hookData.Type);
                    resolvedProbeTypes[hookData.Type] = hook.GetType();
                }
                else
                {
                    hook = objectCreator.GetInstanceOfSubClassOfTByNameFromAssemblies<IProbe>(
                        resolvedProbeType.FullName!,
                        [resolvedProbeType.Assembly]);
                    hook.Context = context;
                }
            }
            catch (ArgumentException e)
            {
                context.Logger.LogCritical(
                    "Encountered exception while loading {HookType} instance {InstanceName} - {Exception}",
                    typeof(IProbe).Name, hookData.Type, e);
                throw;
            }

            var configurationsValidationResults = (hook.LoadAndValidateConfiguration(
                hookData.Configuration) ?? Enumerable.Empty<ValidationResult>()).ToList();
            foreach (var validationResult in configurationsValidationResults)
                validationResult.ErrorMessage = $"In Hook of {typeof(IProbe).Name} named {hookData.Name} of type" +
                                                $" {hookData.Type} {validationResult.ErrorMessage}";

            validationResults.AddRange(configurationsValidationResults);
            loadedHooks.Add(new KeyValuePair<string, IProbe>(hookData.Name, hook));
        }

        return loadedHooks;
    }

    private void ValidateProbeDefinitions()
    {
        foreach (var sessionBuilder in Sessions ?? [])
        {
            foreach (var probeBuilder in sessionBuilder.Probes ?? [])
            {
                if (string.IsNullOrWhiteSpace(sessionBuilder.Name))
                {
                    _validationResults.Add(new ValidationResult("Session name is required when configuring probes."));
                }

                if (string.IsNullOrWhiteSpace(probeBuilder.Name))
                {
                    _validationResults.Add(new ValidationResult(
                        $"Probe name is required for session '{sessionBuilder.Name}'.",
                        [nameof(ProbeBuilder.Name)]));
                }

                if (string.IsNullOrWhiteSpace(probeBuilder.Probe))
                {
                    _validationResults.Add(new ValidationResult(
                        $"Probe type is required for probe '{probeBuilder.Name}' in session '{sessionBuilder.Name}'.",
                        [nameof(ProbeBuilder.Probe)]));
                }
            }
        }
    }

    /// <summary>
    ///     Builds context based the configured values and the context loaded from YAML
    /// </summary>
    private void InitializeContext()
    {
        var existingContext = LoadedContext ? Context : null;
        // Keep metadata available on the execution context for logging/runtime consumers, while
        // configuration validation treats a missing YAML section as an empty metadata object and
        // reports the required Team/System fields.
        var metaData = MetaData ?? new MetaDataConfig();

        var logger = _configuredLogger ?? existingContext?.Logger ?? NullLogger.Instance;
        var caseName = _configuredCaseName ?? existingContext?.CaseName;
        var executionId = _configuredExecutionId ?? existingContext?.ExecutionId;
        var rootConfiguration = existingContext?.RootConfiguration ?? new ConfigurationBuilder().Build();
        var internalRunningSessions = existingContext?.InternalRunningSessions ??
                                      new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>());

        var internalGlobalDict = existingContext?.InternalGlobalDict != null
            ? new Dictionary<string, object?>(existingContext.InternalGlobalDict)
            : new Dictionary<string, object?>();
        foreach (var (key, value) in _globalDict)
        {
            internalGlobalDict[key] = value;
        }

        Context = new InternalContext()
        {
            Logger = logger,
            CaseName = caseName,
            ExecutionId = executionId,
            RootConfiguration = rootConfiguration,
            InternalRunningSessions = internalRunningSessions,
            InternalGlobalDict = internalGlobalDict
        };

        // saved context's metadata in globalDict
        Context.InsertValueIntoGlobalDictionary(Context.GetMetaDataPath(), metaData);
        Context.Logger.LogDebug(
            "Initialized execution context. LoadedContext={LoadedContext}, ExecutionId={ExecutionId}, CaseName={CaseName}, GlobalKeys={GlobalKeyCount}",
            LoadedContext, Context.ExecutionId, Context.CaseName, Context.InternalGlobalDict.Count);
    }

    /// <summary>
    /// Filters Sessions and Assertions lists based on provided flags
    /// </summary>
    private void FilterConfigurationsBasedOnFlags()
    {
        var sessionsBeforeFiltering = Sessions?.Length ?? 0;
        var assertionsBeforeFiltering = Assertions?.Length ?? 0;
        Assertions = (Assertions ?? [])
            .FilterConfigurationByAssertion(_assertionNamesToRun, _assertionCategoriesToRun, Context);
        Sessions = (Sessions ?? []).FilterConfigurationBySessionsAndAssertions(Assertions, _sessionNamesToRun,
            _assertionNamesToRun, _sessionCategoriesToRun, _assertionCategoriesToRun, Context);
        Context.Logger.LogDebug(
            "Filtered execution configuration. Sessions: {SessionCountBefore} -> {SessionCountAfter}, Assertions: {AssertionCountBefore} -> {AssertionCountAfter}",
            sessionsBeforeFiltering, Sessions.Length, assertionsBeforeFiltering, Assertions.Length);
    }

    private void DeduplicateValidationResults()
    {
        if (_validationResults.Count < 2)
        {
            return;
        }

        var distinctValidationResults = _validationResults
            .GroupBy(result => new
            {
                Message = result.ErrorMessage ?? string.Empty,
                MemberNames = string.Join("|", result.MemberNames.OrderBy(memberName => memberName,
                    StringComparer.Ordinal))
            })
            .Select(group => group.First())
            .ToList();

        if (distinctValidationResults.Count == _validationResults.Count)
        {
            return;
        }

        _validationResults.Clear();
        _validationResults.AddRange(distinctValidationResults);
    }

    private void StoreRenderedConfigurationTemplate()
    {
        var includedSessionNames = (Sessions ?? [])
            .Where(session => !string.IsNullOrWhiteSpace(session.Name))
            .Select(session => session.Name!)
            .ToHashSet(StringComparer.Ordinal);
        var assertionStatusesToReport = (Assertions ?? [])
            .Where(assertion => !string.IsNullOrWhiteSpace(assertion.Name))
            .ToDictionary(
                assertion => assertion.Name!,
                assertion => (IReadOnlyList<string>)assertion.StatusesToReport
                    .Select(status => status.ToString())
                    .ToList(),
                StringComparer.Ordinal);
        var renderedTemplate = ConfigurationTemplateRenderer.Render(
            _templateSourceConfiguration ?? Context.RootConfiguration,
            [
                new KeyValuePair<string, object?>("Storages", Storages),
                new KeyValuePair<string, object?>("DataSources", DataSources),
                new KeyValuePair<string, object?>("Sessions", Sessions),
                new KeyValuePair<string, object?>("Assertions", Assertions),
                new KeyValuePair<string, object?>("Links", Links),
                new KeyValuePair<string, object?>("MetaData", MetaData)
            ],
            Infrastructure.Constants.ConfigurationSectionNames,
            includedSessionNames,
            assertionStatusesToReport);

        Context.SetRenderedConfigurationTemplate(renderedTemplate);
    }

    /// <summary>
    /// Builds the configured Runner execution builder output from the current state.
    /// </summary>
    /// <remarks>
    /// Call this after the fluent configuration is complete. The method validates the accumulated state and materializes the runtime or immutable configuration object represented by the builder.
    /// </remarks>
    /// <qaas-docs group="Configuration as Code" subgroup="Executions" />
    public override Execution Build()
    {
        InitializeContext();
        Context.Logger.LogInformation(
            "Started building {Type} execution with executionId {ExecutionId} and case name {CaseName}", Type,
            Context.ExecutionId,
            Context.CaseName);


        // loads all hooks & logics validate them
        var scope = LoadContextScopeDependencies();
        _buildScope = scope;

        try
        {
            // filter session assertion list based on names & categories
            FilterConfigurationsBasedOnFlags();
            StoreRenderedConfigurationTemplate();

            // validate configuration
            ValidateConfiguredSections();
            DeduplicateValidationResults();

            if (_validationResults.Any())
            {
                Context.Logger.LogDebug("Validation produced {ValidationResultCount} result(s)", _validationResults.Count);
                Context.Logger.LogCritical("Configurations are not valid. The validation results are: \n- " +
                                           string.Join("\n- ", _validationResults.Select(result => result.ErrorMessage)));
                throw new InvalidConfigurationsException("Configurations are not valid");
            }

            // builds every list of domain objects
            // Materializing the builders once keeps component counts stable for logging and avoids
            // rebuilding hook-backed objects when the lists are resolved into execution logics.
            var builtDataSources = BuildDataSources(scope).ToList();
            var builtSessions = BuildSessions(scope).ToList();
            var builtStorages = BuildStorages().ToList();
            var builtAssertions = BuildAssertions(scope).ToList();
            var builtReports = BuildReports().ToList();
            var dataSourceLogic =
                scope.Resolve<DataSourceLogic>(
                    new TypedParameter(typeof(IList<DataSource>), builtDataSources));
            var sessionLogic =
                scope.Resolve<SessionLogic>(
                    new TypedParameter(typeof(List<ISession>), builtSessions));
            var storageLogic =
                scope.Resolve<StorageLogic>(new TypedParameter(typeof(IList<IStorage>), builtStorages),
                    new TypedParameter(typeof(ExecutionType), Type));
            var assertionLogic =
                scope.Resolve<AssertionLogic>(
                    new TypedParameter(typeof(IList<Assertion>), builtAssertions));
            var reportLogic =
                scope.Resolve<ReportLogic>(new TypedParameter(typeof(IList<IReporter>), builtReports));
            var templateLogic = scope.Resolve<TemplateLogic>(new TypedParameter(typeof(Context), Context));

            Context.Logger.LogDebug(
                "Resolved execution components. DataSources={DataSourceCount}, Sessions={SessionCount}, Storages={StorageCount}, Assertions={AssertionCount}, Reporters={ReporterCount}",
                builtDataSources.Count, builtSessions.Count, builtStorages.Count, builtAssertions.Count,
                builtReports.Count);

            Context.Logger.LogInformation(
                "Finished building {Type} execution with executionId {ExecutionId} and case name {CaseName}", Type,
                Context.ExecutionId, Context.CaseName);

            // bind back context onto the executionBuilder object
            return new Execution(Type, Context, scope)
            {
                AssertionLogic = assertionLogic, ReportLogic = reportLogic, SessionLogic = sessionLogic,
                TemplateLogic = templateLogic, DataSourceLogic = dataSourceLogic, StorageLogic = storageLogic
            };
        }
        catch
        {
            Context.Logger.LogDebug("Execution build failed. Disposing Autofac scope for execution {ExecutionId}",
                Context.ExecutionId);
            scope.Dispose();
            _buildScope = null;
            throw;
        }
    }

    private static T[]? UpdateByName<T>(T[]? items, string key, T replacement, Func<T, string?> keySelector)
    {
        if (items == null)
        {
            return null;
        }

        var index = Array.FindIndex(items, item => keySelector(item) == key);
        if (index < 0)
        {
            return items;
        }

        items[index] = replacement;
        return items;
    }

    private static T[]? DeleteByName<T>(T[]? items, string key, Func<T, string?> keySelector)
    {
        return items?.Where(item => keySelector(item) != key).ToArray();
    }

    private static T[]? UpdateAt<T>(T[]? items, int index, T replacement)
    {
        if (items == null)
        {
            return null;
        }

        if (index < 0 || index >= items.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        items[index] = replacement;
        return items;
    }

    private static T[]? DeleteAt<T>(T[]? items, int index)
    {
        if (items == null)
        {
            return null;
        }

        if (index < 0 || index >= items.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return items.Where((_, itemIndex) => itemIndex != index).ToArray();
    }

    private void ValidateConfiguredSections()
    {
        TryValidateConfiguredMembers(nameof(DataSources), nameof(Storages), nameof(Assertions), nameof(Links),
            nameof(MetaData), nameof(Sessions));

        ValidateCollection(DataSources, nameof(DataSources));
        ValidateCollection(Storages, nameof(Storages));
        ValidateCollection(Assertions, nameof(Assertions));
        ValidateCollection(Links, nameof(Links));
        ValidateCollection(Sessions, nameof(Sessions));

        _ = TryValidateConfiguredObjectRecursive(MetaData ?? new MetaDataConfig(), _validationResults,
            nameof(MetaData));
    }

    private void ValidateCollection<T>(IEnumerable<T>? items, string parentPath)
    {
        if (items == null)
        {
            return;
        }

        var index = 0;
        foreach (var item in items)
        {
            if (item != null)
            {
                _ = TryValidateConfiguredObjectRecursive(item, _validationResults, $"{parentPath}:{index}");
            }

            index++;
        }
    }

    private void TryValidateConfiguredMembers(params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames.Distinct(StringComparer.Ordinal))
        {
            // ExecutionBuilder keeps configuration sections as mutable builder members, some of which are not writable
            // through the public fluent surface. DataAnnotations can validate those members once we have the property
            // metadata, but there is no framework API that says "validate exactly these named non-public members".
            // Reflection is therefore limited to the runner's configuration boundary instead of recursing through the
            // entire builder object graph the generic framework validator would otherwise inspect.
            var property = GetType().GetProperty(propertyName, ValidationBindingFlags);
            if (property == null || property.GetIndexParameters().Length > 0 ||
                !TryGetPropertyValue(this, property, out var propertyValue))
            {
                continue;
            }

            var validationContext = new ValidationContext(this, null, null)
            {
                MemberName = property.Name
            };

            foreach (var validationAttribute in property.GetCustomAttributes<ValidationAttribute>())
            {
                var validationResult = validationAttribute.GetValidationResult(propertyValue, validationContext);
                if (validationResult != ValidationResult.Success && validationResult != null)
                {
                    _validationResults.Add(validationResult);
                }
            }
        }
    }

    private static bool TryValidateConfiguredObjectRecursive(object? obj, List<ValidationResult> results,
        string parentPath = "")
    {
        if (obj == null)
        {
            return true;
        }

        if (IsTerminalType(obj.GetType()))
        {
            var terminalResults = new List<ValidationResult>();
            _ = TryValidateCurrentObject(obj, terminalResults);
            results.AddRange(PrefixValidationResults(terminalResults, parentPath));
            return !terminalResults.Any();
        }

        var localResults = new List<ValidationResult>();
        var isValid = TryValidateCurrentObject(obj, localResults);
        results.AddRange(PrefixValidationResults(localResults, parentPath));

        var properties = obj.GetType()
            .GetProperties(ValidationBindingFlags)
            .Where(property => property.GetIndexParameters().Length == 0 &&
                               property.PropertyType != obj.GetType() &&
                               ShouldInspectProperty(property));

        foreach (var property in properties)
        {
            if (!TryGetPropertyValue(obj, property, out var value))
            {
                continue;
            }

            var propertyPath = $"{parentPath}{ConfigurationConstants.PathSeparator}{property.Name}";
            if (value is IEnumerable enumerableValue && value is not string)
            {
                if (value is IDictionary dictionary)
                {
                    foreach (var key in dictionary.Keys)
                    {
                        var entry = dictionary[key];
                        if (entry == null)
                        {
                            continue;
                        }

                        var entryPath = $"{propertyPath}{ConfigurationConstants.PathSeparator}{key}";
                        if (!TryValidateConfiguredObjectRecursive(entry, results, entryPath))
                        {
                            isValid = false;
                        }
                    }
                }
                else
                {
                    var index = 0;
                    foreach (var item in enumerableValue)
                    {
                        if (item != null &&
                            !TryValidateConfiguredObjectRecursive(item, results,
                                $"{propertyPath}{ConfigurationConstants.PathSeparator}{index}"))
                        {
                            isValid = false;
                        }

                        index++;
                    }
                }
            }
            else if (value != null && !IsTerminalType(value.GetType()) &&
                     !TryValidateConfiguredObjectRecursive(value, results, propertyPath))
            {
                isValid = false;
            }
        }

        return isValid;
    }

    private static bool TryValidateCurrentObject(object obj, List<ValidationResult> results)
    {
        var validationResults = new List<ValidationResult>();
        var objectType = obj.GetType();

        if (IsTerminalType(objectType))
        {
            var validationContext = new ValidationContext(obj, null, null)
            {
                MemberName = string.Empty
            };

            foreach (var validationAttribute in objectType.GetCustomAttributes<ValidationAttribute>())
            {
                var result = validationAttribute.GetValidationResult(obj, validationContext);
                if (result != ValidationResult.Success && result != null)
                {
                    validationResults.Add(result);
                }
            }
        }
        else
        {
            _ = Validator.TryValidateObject(obj, new ValidationContext(obj), validationResults, true);
            var objectValidationContext = new ValidationContext(obj, null, null)
            {
                MemberName = string.Empty
            };

            foreach (var validationAttribute in objectType.GetCustomAttributes<ValidationAttribute>())
            {
                var result = validationAttribute.GetValidationResult(obj, objectValidationContext);
                if (result != ValidationResult.Success && result != null)
                {
                    validationResults.Add(result);
                }
            }

            foreach (var property in objectType.GetProperties(ValidationBindingFlags)
                         .Where(property => property.GetMethod?.IsPublic != true))
            {
                var validationAttributes = property.GetCustomAttributes<ValidationAttribute>().ToArray();
                if (property.GetIndexParameters().Length > 0 || validationAttributes.Length == 0 ||
                    !TryGetPropertyValue(obj, property, out var propertyValue))
                {
                    continue;
                }

                var validationContext = new ValidationContext(obj, null, null)
                {
                    MemberName = property.Name
                };

                foreach (var validationAttribute in validationAttributes)
                {
                    var result = validationAttribute.GetValidationResult(propertyValue, validationContext);
                    if (result != ValidationResult.Success && result != null)
                    {
                        validationResults.Add(result);
                    }
                }
            }
        }

        results.AddRange(DistinctValidationResults(validationResults));
        return !validationResults.Any();
    }

    private static IEnumerable<ValidationResult> PrefixValidationResults(IEnumerable<ValidationResult> validationResults,
        string parentPath)
    {
        var trimmedParentPath = parentPath.TrimStart(ConfigurationConstants.PathSeparator.ToCharArray());
        var parentPrefix = trimmedParentPath.Length == 0 ? string.Empty : $"{trimmedParentPath} - ";

        return validationResults.Select(result =>
        {
            result.ErrorMessage = $"{parentPrefix}{result.ErrorMessage}";
            return result;
        });
    }

    private static IEnumerable<ValidationResult> DistinctValidationResults(
        IEnumerable<ValidationResult> validationResults)
    {
        return validationResults
            .GroupBy(result => new
            {
                Message = result.ErrorMessage ?? string.Empty,
                Members = string.Join("|", result.MemberNames.OrderBy(member => member, StringComparer.Ordinal))
            })
            .Select(group => group.First());
    }

    private static bool TryGetPropertyValue(object instance, PropertyInfo property, out object? value)
    {
        try
        {
            value = property.GetValue(instance);
            return true;
        }
        catch (TargetInvocationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is MethodAccessException or ArgumentException)
        {
            var getter = property.GetGetMethod(nonPublic: true);
            if (getter == null)
            {
                value = null;
                return false;
            }

            value = getter.Invoke(instance, null);
            return true;
        }
    }

    private static bool IsTerminalType(Type type)
    {
        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;

        return effectiveType.IsPrimitive
               || effectiveType.IsEnum
               || effectiveType == typeof(string)
               || effectiveType == typeof(decimal)
               || effectiveType == typeof(DateTime)
               || effectiveType == typeof(DateTimeOffset)
               || effectiveType == typeof(TimeSpan)
               || effectiveType == typeof(Guid)
               || effectiveType == typeof(Uri)
               || effectiveType == typeof(Type)
               || typeof(Delegate).IsAssignableFrom(effectiveType)
               || typeof(MemberInfo).IsAssignableFrom(effectiveType)
               || typeof(Assembly).IsAssignableFrom(effectiveType)
               || effectiveType == typeof(IntPtr)
               || effectiveType == typeof(UIntPtr)
               || effectiveType.IsPointer
               || effectiveType.IsByRef;
    }

    private static bool ShouldInspectProperty(PropertyInfo property)
    {
        return property.GetCustomAttributes<ValidationAttribute>().Any()
               || property.GetCustomAttributes<DescriptionAttribute>().Any()
               || property.GetCustomAttributes<DefaultValueAttribute>().Any();
    }
}
