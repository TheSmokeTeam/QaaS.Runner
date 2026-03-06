using System.Collections.Immutable;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
        var resolvedDataSources = configuredDataSources.Select(dataSourceBuilder =>
            dataSourceBuilder.Build(Context, dataSources,
                scope.Resolve<IList<KeyValuePair<string, IGenerator>>>()));
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

    public ExecutionBuilder WithGlobalDict(Dictionary<string, object?> globalDict)
    {
        _globalDict = globalDict;
        return this;
    }

    public ExecutionBuilder AddSession(SessionBuilder sessionBuilder)
    {
        Sessions = Sessions is null ? [sessionBuilder] : Sessions.Append(sessionBuilder).ToArray();
        return this;
    }

    public ExecutionBuilder CreateSession(SessionBuilder sessionBuilder)
    {
        return AddSession(sessionBuilder);
    }

    public IReadOnlyList<SessionBuilder> ReadSessions()
    {
        return Sessions ?? [];
    }

    public ExecutionBuilder UpdateSession(string sessionName, SessionBuilder sessionBuilder)
    {
        Sessions = UpdateByName(Sessions, sessionName, sessionBuilder, session => session.Name);
        return this;
    }

    public ExecutionBuilder DeleteSession(string sessionName)
    {
        Sessions = DeleteByName(Sessions, sessionName, session => session.Name);
        return this;
    }

    public ExecutionBuilder AddAssertion(AssertionBuilder assertionBuilder)
    {
        Assertions = Assertions is null ? [assertionBuilder] : Assertions.Append(assertionBuilder).ToArray();
        return this;
    }

    public ExecutionBuilder CreateAssertion(AssertionBuilder assertionBuilder)
    {
        return AddAssertion(assertionBuilder);
    }

    public IReadOnlyList<AssertionBuilder> ReadAssertions()
    {
        return Assertions ?? [];
    }

    public ExecutionBuilder UpdateAssertion(string assertionName, AssertionBuilder assertionBuilder)
    {
        Assertions = UpdateByName(Assertions, assertionName, assertionBuilder, assertion => assertion.Name);
        return this;
    }

    public ExecutionBuilder DeleteAssertion(string assertionName)
    {
        Assertions = DeleteByName(Assertions, assertionName, assertion => assertion.Name);
        return this;
    }

    public ExecutionBuilder AddStorage(StorageBuilder storageBuilder)
    {
        Storages = Storages is null ? [storageBuilder] : Storages.Append(storageBuilder).ToArray();
        return this;
    }

    public ExecutionBuilder CreateStorage(StorageBuilder storageBuilder)
    {
        return AddStorage(storageBuilder);
    }

    public IReadOnlyList<StorageBuilder> ReadStorages()
    {
        return Storages ?? [];
    }

    public ExecutionBuilder UpdateStorageAt(int index, StorageBuilder storageBuilder)
    {
        Storages = UpdateAt(Storages, index, storageBuilder);
        return this;
    }

    public ExecutionBuilder DeleteStorageAt(int index)
    {
        Storages = DeleteAt(Storages, index);
        return this;
    }

    public ExecutionBuilder AddDataSource(DataSourceBuilder dataSourceBuilder)
    {
        DataSources = DataSources is null ? [dataSourceBuilder] : DataSources.Append(dataSourceBuilder).ToArray();
        return this;
    }

    public ExecutionBuilder CreateDataSource(DataSourceBuilder dataSourceBuilder)
    {
        return AddDataSource(dataSourceBuilder);
    }

    public IReadOnlyList<DataSourceBuilder> ReadDataSources()
    {
        return DataSources ?? [];
    }

    public ExecutionBuilder UpdateDataSource(string dataSourceName, DataSourceBuilder dataSourceBuilder)
    {
        DataSources = UpdateByName(DataSources, dataSourceName, dataSourceBuilder, source => source.Name);
        return this;
    }

    public ExecutionBuilder DeleteDataSource(string dataSourceName)
    {
        DataSources = DeleteByName(DataSources, dataSourceName, source => source.Name);
        return this;
    }

    public ExecutionBuilder AddLink(LinkBuilder linkBuilder)
    {
        Links = Links is null ? [linkBuilder] : Links.Append(linkBuilder).ToArray();
        return this;
    }

    public ExecutionBuilder CreateLink(LinkBuilder linkBuilder)
    {
        return AddLink(linkBuilder);
    }

    public IReadOnlyList<LinkBuilder> ReadLinks()
    {
        return Links ?? [];
    }

    public ExecutionBuilder UpdateLinkAt(int index, LinkBuilder linkBuilder)
    {
        Links = UpdateAt(Links, index, linkBuilder);
        return this;
    }

    public ExecutionBuilder DeleteLinkAt(int index)
    {
        Links = DeleteAt(Links, index);
        return this;
    }

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

    public ExecutionBuilder SetCase(string caseName)
    {
        _configuredCaseName = caseName;
        return this;
    }

    public ExecutionBuilder SetExecutionId(string executionId)
    {
        _configuredExecutionId = executionId;
        return this;
    }

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
            containerBuilder.RegisterModule(
                new HooksLoaderModule<IProbe>(_validationResults)); // Loads all IProbe hooks

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
        // Missing MetaData in YAML should behave like an empty section at runtime, but the builder
        // keeps the property null so configuration validation does not fabricate required-field
        // failures for a section that was never provided by the user.
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
    /// <inheritdoc />
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
            _ = ValidationUtils.TryValidateObjectRecursive(this, _validationResults);
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
}
