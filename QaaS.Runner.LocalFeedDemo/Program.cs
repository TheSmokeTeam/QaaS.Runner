using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Text;
using QaaS.Common.Generators.ConfigurationObjects.FromExternalSourceConfigurations;
using QaaS.Common.Generators.FromExternalSourceGenerators;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.LocalFeedDemo;

internal static class Program
{
    private static int Main(string[] args)
    {
        var baseDirectory = AppContext.BaseDirectory;
        Directory.SetCurrentDirectory(baseDirectory);

        var runnerArguments = args.Length == 0
            ? ["run", Path.Combine(baseDirectory, "path-only-from-filesystem.qaas.yaml"), "--no-env", "--no-process-exit"]
            : args;

        Console.WriteLine($"Running local feed demo with arguments: {string.Join(' ', runnerArguments)}");

        var runner = QaaS.Runner.Bootstrap.New(runnerArguments);
        runner.ExitProcessOnCompletion = false;

        return runner.RunAndGetExitCode();
    }
}

public sealed record GeneratedFileSetAssertionConfiguration
{
    [Required]
    public string DataSourceName { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int ExpectedCount { get; init; }

    [Required, MinLength(1)]
    public string[] ExpectedStorageKeys { get; init; } = [];
}

public sealed class GeneratedFileSetAssertion : BaseAssertion<GeneratedFileSetAssertionConfiguration>
{
    private static readonly string[] ExpectedBodies =
    [
        "{\"id\":1,\"name\":\"first\"}",
        "{\"id\":2,\"name\":\"second\"}",
        "{\"id\":10,\"name\":\"third\"}"
    ];

    public override bool Assert(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        var dataSource = dataSourceList.SingleOrDefault(dataSource =>
            string.Equals(dataSource.Name, Configuration.DataSourceName, StringComparison.Ordinal));

        if (dataSource == null)
        {
            AssertionMessage = $"Data source '{Configuration.DataSourceName}' was not found.";
            AssertionTrace = $"AvailableDataSources=[{string.Join(", ", dataSourceList.Select(item => item.Name))}]";
            return false;
        }

        if (dataSource.Generator is not FromFileSystem generator)
        {
            AssertionMessage = $"Data source '{Configuration.DataSourceName}' did not resolve to FromFileSystem.";
            AssertionTrace = $"ResolvedGenerator={dataSource.Generator.GetType().FullName}";
            return false;
        }

        var generatedData = dataSource.Retrieve().ToList();
        var actualStorageKeys = generatedData
            .Select(data => data.MetaData?.Storage?.Key ?? string.Empty)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        var actualBodies = generatedData
            .Select(data => DecodeBody(data.Body))
            .OrderBy(body => body, StringComparer.Ordinal)
            .ToArray();
        var expectedStorageKeys = Configuration.ExpectedStorageKeys
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        var expectedBodies = ExpectedBodies
            .OrderBy(body => body, StringComparer.Ordinal)
            .ToArray();
        var usesUnorderedDefault = generator.Configuration.DataArrangeOrder == DataArrangeOrder.Unordered;

        var passed = usesUnorderedDefault
                     && generatedData.Count == Configuration.ExpectedCount
                     && actualStorageKeys.SequenceEqual(expectedStorageKeys, StringComparer.Ordinal)
                     && actualBodies.SequenceEqual(expectedBodies, StringComparer.Ordinal);

        AssertionMessage = passed
            ? "Local feed runner/common-generators demo passed."
            : "Local feed runner/common-generators demo failed.";
        AssertionTrace =
            $"ArrangeOrder={generator.Configuration.DataArrangeOrder}; Count={generatedData.Count}; " +
            $"StorageKeys=[{string.Join(", ", actualStorageKeys)}]; Bodies=[{string.Join(", ", actualBodies)}]";

        return passed;
    }

    private static string DecodeBody(object? body)
    {
        return body switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string text => text,
            null => string.Empty,
            _ => body.ToString() ?? string.Empty
        };
    }
}
