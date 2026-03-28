internal sealed record SupportPaths(
    string CasesDirectory,
    string ReportsDirectory,
    string SupportDirectory,
    string InputDirectory,
    string FoldersDirectory,
    string SampleFilePath,
    string CertificatePath,
    string SchemaPath,
    string RunnerExecuteInnerPath,
    string SupportDataSourceName,
    string SupportSessionName,
    string SupportStubName);

internal sealed record HookDefinition(string FullName, string DisplayName, Type HookType, Type? ConfigurationType);

internal sealed record HookCatalog(IReadOnlyList<HookDefinition> Hooks, HookDefinition Default);

internal sealed record HookInventory(
    HookCatalog Generators,
    HookCatalog Probes,
    HookCatalog Assertions,
    HookCatalog Processors);

internal sealed record ContextSpec(
    string Domain,
    string Name,
    Type TargetType,
    ValidationKind ValidationKind,
    string FileStem,
    string FileExtension,
    Func<object?, HookInventory, SupportPaths, Dictionary<string, object?>> CreateRoot,
    IReadOnlyDictionary<Type, string>? SelectedVariants = null,
    IReadOnlyCollection<string>? AlwaysIncludeRootProperties = null,
    IReadOnlyCollection<string>? TerminalProperties = null,
    IReadOnlyDictionary<string, object?>? BaselineOverrides = null,
    IReadOnlyDictionary<string, object?>? RequiredOnlyOverrides = null,
    IReadOnlyCollection<string>? RequiredOnlyRemovals = null,
    IReadOnlyDictionary<string, object?>? FilledOverrides = null,
    IReadOnlyCollection<string>? FilledRemovals = null)
{
    public IReadOnlyDictionary<Type, string> SelectedVariants { get; } =
        SelectedVariants ?? new Dictionary<Type, string>();

    public IReadOnlyCollection<string> AlwaysIncludeRootProperties { get; } =
        new HashSet<string>(AlwaysIncludeRootProperties ?? Array.Empty<string>(), StringComparer.Ordinal);

    public IReadOnlyCollection<string> TerminalProperties { get; } =
        new HashSet<string>(TerminalProperties ?? Array.Empty<string>(), StringComparer.Ordinal);

    public IReadOnlyDictionary<string, object?> BaselineOverrides { get; } =
        BaselineOverrides ?? new Dictionary<string, object?>();

    public IReadOnlyDictionary<string, object?> RequiredOnlyOverrides { get; } =
        RequiredOnlyOverrides ?? new Dictionary<string, object?>();

    public IReadOnlyCollection<string> RequiredOnlyRemovals { get; } =
        new HashSet<string>(RequiredOnlyRemovals ?? Array.Empty<string>(), StringComparer.Ordinal);

    public IReadOnlyDictionary<string, object?> FilledOverrides { get; } =
        FilledOverrides ?? new Dictionary<string, object?>();

    public IReadOnlyCollection<string> FilledRemovals { get; } =
        new HashSet<string>(FilledRemovals ?? Array.Empty<string>(), StringComparer.Ordinal);
}

internal sealed record FieldDescriptor(
    string DisplayPath,
    IReadOnlyList<PathToken> Tokens,
    Type FieldType,
    Type OwnerType,
    System.Reflection.PropertyInfo Property);

internal readonly record struct PathToken(PathTokenKind Kind, string? Name, int Index)
{
    public static PathToken Property(string name) => new(PathTokenKind.Property, name, -1);
    public static PathToken Item(int index) => new(PathTokenKind.Item, null, index);
}

internal enum PathTokenKind
{
    Property,
    Item
}

internal enum ValidationKind
{
    RunnerTemplate,
    RunnerExecute,
    MockerTemplate
}

internal enum SweepMode
{
    Value,
    Blank
}

internal sealed record ValidationOutcome(bool Success, int ExitCode, string Summary, string Output);

internal sealed record CaseResult(
    string CaseId,
    string Domain,
    string Context,
    string FieldPath,
    SweepMode Mode,
    bool Success,
    int ExitCode,
    string Summary,
    string ConfigPath,
    string OutputSnippet);
