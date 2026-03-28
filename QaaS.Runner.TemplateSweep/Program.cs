using QaaS.Framework.SDK;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Hooks.Generator;
using QaaS.Framework.SDK.Hooks.Processor;
using QaaS.Framework.SDK.Hooks.Probe;
using System.Reflection;
using System.Runtime.Loader;

return SweepProgram.Run();

/// <summary>
/// Generates positive runner template cases and verifies that every generated case templates successfully.
/// </summary>
internal static class SweepProgram
{
    public static int Run()
    {
        var solutionRoot = SweepWorkspace.FindSolutionRoot();
        var support = SweepWorkspace.PrepareSupportPaths(solutionRoot);
        var hooks = HookDiscovery.Discover();
        var contexts = SweepRunnerContexts.Build(hooks, support)
            .Where(context => context.ValidationKind == ValidationKind.RunnerTemplate)
            .ToList();
        var results = new List<TemplateCaseResult>();
        var caseCounter = 0;

        Console.WriteLine($"Solution root: {solutionRoot}");
        Console.WriteLine($"Contexts: {contexts.Count}");

        foreach (var context in contexts)
        {
            Console.WriteLine($"[{context.Name}] generating required-only and filled cases");

            foreach (var variant in new[] { TemplateCaseVariant.RequiredOnly, TemplateCaseVariant.Filled })
            {
                caseCounter++;
                var caseId = $"{caseCounter:00000}";
                var suffix = variant == TemplateCaseVariant.RequiredOnly ? "required-only" : "filled";
                var configPath = Path.Combine(
                    support.CasesDirectory,
                    $"{caseId}-{context.FileStem}-{suffix}.{context.FileExtension}");

                var document = variant == TemplateCaseVariant.RequiredOnly
                    ? SweepContexts.BuildRequiredOnlyDocument(context, hooks, support)
                    : SweepContexts.BuildFilledDocument(context, hooks, support);
                File.WriteAllText(configPath, SweepWorkspace.Serialize(document));

                var outcome = SweepValidation.Validate(context.ValidationKind, configPath);
                results.Add(new TemplateCaseResult(
                    caseId,
                    context.Name,
                    variant,
                    outcome.Success,
                    outcome.ExitCode,
                    outcome.Summary,
                    configPath,
                    SweepWorkspace.Truncate(outcome.Output, 400)));
            }
        }

        var markdownReportPath = Path.Combine(support.ReportsDirectory, "runner-template-sweep-report.md");
        var jsonReportPath = Path.Combine(support.ReportsDirectory, "runner-template-sweep-report.json");
        File.WriteAllText(markdownReportPath, BuildMarkdownReport(results, hooks, contexts.Count));
        File.WriteAllText(jsonReportPath, System.Text.Json.JsonSerializer.Serialize(results,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine();
        Console.WriteLine($"Cases: {results.Count}");
        Console.WriteLine($"Failures: {results.Count(result => !result.Success)}");
        Console.WriteLine($"Successes: {results.Count(result => result.Success)}");
        Console.WriteLine($"Markdown report: {markdownReportPath}");
        Console.WriteLine($"JSON report: {jsonReportPath}");

        return results.Any(result => !result.Success) ? 1 : 0;
    }

    private static string BuildMarkdownReport(
        IReadOnlyList<TemplateCaseResult> results,
        HookInventory hooks,
        int contextCount)
    {
        var failures = results.Where(result => !result.Success).ToList();
        var successes = results.Where(result => result.Success).ToList();
        var lines = new List<string>
        {
            "# Runner Template Sweep Report",
            string.Empty,
            $"Generated: {DateTimeOffset.Now:O}",
            $"Contexts: {contextCount}",
            $"Cases: {results.Count}",
            $"Failures: {failures.Count}",
            $"Successes: {successes.Count}",
            string.Empty,
            "## Hooks",
            $"- Generators: {string.Join(", ", hooks.Generators.Hooks.Select(hook => hook.DisplayName))}",
            $"- Probes: {string.Join(", ", hooks.Probes.Hooks.Select(hook => hook.DisplayName))}",
            $"- Assertions: {string.Join(", ", hooks.Assertions.Hooks.Select(hook => hook.DisplayName))}",
            string.Empty,
            "## Failures"
        };

        if (failures.Count == 0)
        {
            lines.Add("- None");
        }
        else
        {
            foreach (var failure in failures.OrderBy(result => result.Context, StringComparer.Ordinal)
                         .ThenBy(result => result.Variant))
            {
                lines.Add(
                    $"- `{failure.Context}` `{failure.Variant}` exit={failure.ExitCode} config=`{failure.ConfigPath}`");
                lines.Add($"  {failure.Summary.Replace(Environment.NewLine, " ")}");
            }
        }

        lines.Add(string.Empty);
        lines.Add("## Successes");

        foreach (var success in successes.OrderBy(result => result.Context, StringComparer.Ordinal)
                     .ThenBy(result => result.Variant))
        {
            lines.Add(
                $"- `{success.Context}` `{success.Variant}` exit={success.ExitCode} config=`{success.ConfigPath}`");
        }

        return string.Join(Environment.NewLine, lines);
    }
}

internal enum TemplateCaseVariant
{
    RequiredOnly,
    Filled
}

internal sealed record TemplateCaseResult(
    string CaseId,
    string Context,
    TemplateCaseVariant Variant,
    bool Success,
    int ExitCode,
    string Summary,
    string ConfigPath,
    string OutputSnippet);

internal static class HookDiscovery
{
    public static HookInventory Discover()
    {
        LoadQaasAssemblies();

        var generators = DiscoverCatalog<IGenerator>(typeof(QaaS.Framework.SDK.Hooks.Generator.BaseGenerator<>),
            "FromFileSystem");
        var probes = DiscoverCatalog<IProbe>(typeof(QaaS.Framework.SDK.Hooks.Probe.BaseProbe<>), "FlushDbRedis");
        var assertions = DiscoverCatalog<IAssertion>(typeof(QaaS.Framework.SDK.Hooks.Assertion.BaseAssertion<>),
            "HttpStatus");
        var processors = DiscoverCatalog<ITransactionProcessor>(
            typeof(QaaS.Framework.SDK.Hooks.Processor.BaseTransactionProcessor<>),
            "StaticResponseProcessor");

        return new HookInventory(generators, probes, assertions, processors);
    }

    private static void LoadQaasAssemblies()
    {
        var loadedAssemblyPaths = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(assembly => assembly.Location)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in new[] { "QaaS*.dll", "Qaas*.dll" })
        {
            foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, pattern))
            {
                if (loadedAssemblyPaths.Contains(path))
                {
                    continue;
                }

                try
                {
                    AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                    loadedAssemblyPaths.Add(path);
                }
                catch
                {
                    // Best effort: hook discovery will ignore assemblies that still fail to load.
                }
            }
        }
    }

    private static HookCatalog DiscoverCatalog<TContract>(Type genericBaseType, string preferredDefaultName)
    {
        var hooks = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly =>
            {
                var name = assembly.GetName().Name ?? string.Empty;
                return name.StartsWith("QaaS.", StringComparison.Ordinal) ||
                       name.StartsWith("Qaas.", StringComparison.Ordinal);
            })
            .SelectMany(GetLoadableTypes)
            .Where(type =>
                type is { IsAbstract: false, IsInterface: false } &&
                !type.ContainsGenericParameters &&
                typeof(TContract).IsAssignableFrom(type))
            .Select(type => new HookDefinition(
                type.FullName ?? type.Name,
                type.Name,
                type,
                ResolveConfigurationType(type, genericBaseType)))
            .Where(hook => hook.ConfigurationType != null)
            .OrderBy(hook => hook.DisplayName, StringComparer.Ordinal)
            .ToList();

        var preferred = hooks.FirstOrDefault(hook =>
            string.Equals(hook.DisplayName, preferredDefaultName, StringComparison.Ordinal));

        return new HookCatalog(hooks, preferred ?? hooks.First());
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type != null)!;
        }
    }

    private static Type? ResolveConfigurationType(Type concreteType, Type genericBaseType)
    {
        for (var current = concreteType; current != null && current != typeof(object); current = current.BaseType!)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == genericBaseType)
            {
                return current.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
