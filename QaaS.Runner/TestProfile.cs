using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using QaaS.Framework.Executions;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Runner.Cases;

namespace QaaS.Runner;

/// <summary>
/// Represents a test execution profile that manages runner initialization,
/// remote and local YAML module composition, profile variables, baseline mutations,
/// and test case expansion.
/// </summary>
public abstract class TestProfile
{
    private Runner? _runner;
    private IConfiguration? _variables;
    private ExecutionBuilder? _baseExecutionBuilder;
    private readonly object _initLock = new();
    private bool _isInitialized;
    private bool _hasExecuted;

    /// <summary>
    /// Gets or sets the underlying QaaS Runner instance for this profile.
    /// </summary>
    public Runner Runner
    {
        get
        {
            EnsureInitialized();
            return _runner!;
        }
        protected set => _runner = value;
    }

    /// <summary>
    /// Gets the compiled, flattened variables configuration for this profile.
    /// </summary>
    public IConfiguration Variables
    {
        get
        {
            EnsureInitialized();
            return _variables!;
        }
        protected set => _variables = value;
    }

    /// <summary>
    /// The target test environment name (e.g., local, k8s, dev).
    /// </summary>
    protected readonly string _testEnv;

    /// <summary>
    /// Whether the BaseCase should be automatically applied to the BaseExecutionBuilder.
    /// </summary>
    protected readonly bool _addBaseCase;

    /// <summary>
    /// Optional custom command keyword (e.g. 'run', 'act', 'assert').
    /// </summary>
    protected readonly string _customCommandKeyword;

    /// <summary>
    /// The base execution builder extracted from Bootstrap.New or initialized for the profile.
    /// </summary>
    protected ExecutionBuilder BaseExecutionBuilder
    {
        get
        {
            EnsureInitialized();
            return _baseExecutionBuilder!;
        }
        set => _baseExecutionBuilder = value;
    }

    /// <summary>
    /// The list of test cases to be expanded and executed within this profile.
    /// </summary>
    protected virtual List<ITestCase> TestCases { get; set; } = new();

    /// <summary>
    /// An optional base case that runs against the BaseExecutionBuilder before profile-specific setup and case expansion.
    /// </summary>
    protected virtual ITestCase? BaseCase => null;

    /// <summary>
    /// The YAML files used to build the flattened profile variables.
    /// Defaults to Variables/base.yaml and Variables/{_testEnv}.yaml.
    /// </summary>
    protected virtual string[] VariablesYamlFiles =>
    [
        "Variables/base.yaml",
        $"Variables/{_testEnv}.yaml"
    ];

    /// <summary>
    /// Profile-specific YAML configuration files or remote Artifactory module URLs.
    /// </summary>
    protected virtual string[] ProfileSpecificYamlFiles => Array.Empty<string>();

    /// <summary>
    /// Initializes a new instance of the <see cref="TestProfile"/> class.
    /// </summary>
    /// <param name="testEnv">The target test environment (e.g., local, k8s, dev).</param>
    /// <param name="addBaseCase">Whether to apply the base case before profile-specific setup.</param>
    /// <param name="customCommandKeyword">Optional custom command keyword (defaults to 'run').</param>
    public TestProfile(string testEnv, bool addBaseCase = true, string customCommandKeyword = "")
    {
        _testEnv = testEnv;
        _addBaseCase = addBaseCase;
        _customCommandKeyword = customCommandKeyword;
    }

    /// <summary>
    /// Ensures that profile runner initialization and configuration loading have run after derived construction.
    /// </summary>
    protected void EnsureInitialized()
    {
        if (_isInitialized) return;
        lock (_initLock)
        {
            if (_isInitialized) return;
            _isInitialized = true;
            Initialize();
        }
    }

    private void Initialize()
    {
        var isFileless = false;
        if (_runner == null)
        {
            var runnerInitArgs = SetupRunnerInitArgs(_customCommandKeyword);
            if (runnerInitArgs.Length > 0)
            {
                _runner = Bootstrap.New(runnerInitArgs);
            }
            else
            {
                isFileless = true;
                _runner = Bootstrap.CreateDefaultRunner<Runner>();
            }
            _runner.ExitProcessOnCompletion = false;
        }

        if (_baseExecutionBuilder == null)
        {
            _baseExecutionBuilder = _runner.ExtractBaseBuilder();
            if (isFileless)
            {
                var executionType = _customCommandKeyword.ToLowerInvariant() switch
                {
                    "act" => ExecutionType.Act,
                    "assert" => ExecutionType.Assert,
                    "template" => ExecutionType.Template,
                    _ => ExecutionType.Run
                };
                _baseExecutionBuilder.ExecutionType(executionType);
            }
        }

        if (_variables == null)
        {
            _variables = BuildVariablesConfiguration();
        }
    }

    /// <summary>
    /// Executes the full profile lifecycle and returns the resulting exit code:
    /// 1. Applies BaseCase if enabled.
    /// 2. Applies profile-specific baseline modifications.
    /// 3. Clones and expands TestCases with their logical names.
    /// 4. Runs the runner.
    /// </summary>
    /// <returns>The process exit code produced by the runner.</returns>
    /// <exception cref="InvalidOperationException">Thrown if called more than once on the same profile instance.</exception>
    public virtual int Execute()
    {
        if (_hasExecuted)
            throw new InvalidOperationException("A TestProfile instance can only be executed once. Create a new instance for subsequent runs.");

        _hasExecuted = true;
        EnsureInitialized();

        if (_addBaseCase && BaseCase != null)
        {
            BaseCase.ModifyCaseExecutionBuilder(BaseExecutionBuilder, Variables);
        }

        SetupBaseExecutionBuilder(BaseExecutionBuilder);
        IncludeTestCases();
        return Runner.RunAndGetExitCode();
    }

    /// <summary>
    /// Can be overridden in each TestProfile to change/add stuff to the BaseExecutionBuilder
    /// that will serve as the base for all the cases of the profile.
    /// </summary>
    /// <param name="builder">The base execution builder to configure.</param>
    protected virtual void SetupBaseExecutionBuilder(ExecutionBuilder builder) { }

    /// <summary>
    /// Applies the shared baseline setup logic to the supplied execution builder.
    /// Useful when one profile reuses another profile's baseline mutations.
    /// </summary>
    public void ApplySharedBebSetup(ExecutionBuilder builder) => SetupBaseExecutionBuilder(builder);

    /// <summary>
    /// Builds the flattened variables configuration from the runner's resolved root configuration,
    /// stripping the 'variables:' prefix.
    /// </summary>
    protected virtual IConfiguration BuildVariablesConfiguration()
    {
        var rootConfig = _baseExecutionBuilder?.RootConfiguration;
        if (rootConfig == null)
        {
            return new ConfigurationBuilder().Build();
        }

        var variablesSection = rootConfig.GetSection("variables");
        var flattened = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in variablesSection.AsEnumerable())
        {
            var key = kv.Key.StartsWith("variables:", StringComparison.OrdinalIgnoreCase)
                ? kv.Key.Substring("variables:".Length)
                : kv.Key;

            if (!string.IsNullOrEmpty(key))
            {
                flattened[key] = kv.Value;
            }
        }

        var variablesBuilder = new ConfigurationBuilder();
        variablesBuilder.AddInMemoryCollection(flattened);
        return variablesBuilder.Build();
    }

    private string[] SetupRunnerInitArgs(string customCommandKeyword = "")
    {
        var argList = new List<string>();
        if (ProfileSpecificYamlFiles.Length <= 0 && VariablesYamlFiles.Length <= 0)
            return argList.ToArray();

        if (string.IsNullOrEmpty(customCommandKeyword))
            customCommandKeyword = "run";
        argList.Add(customCommandKeyword);

        foreach (var file in ProfileSpecificYamlFiles)
            AddRunnerInitArg(argList, file);

        foreach (var file in VariablesYamlFiles)
            AddRunnerInitArg(argList, file);

        return argList.ToArray();
    }

    private void AddRunnerInitArg(ICollection<string> argList, string arg)
    {
        if (argList.Count >= 2)
            argList.Add("-w");
        argList.Add(arg);
    }

    private void IncludeTestCases()
    {
        Runner.AddTestCases(BaseExecutionBuilder, Variables, TestCases.ToArray());
    }
}
