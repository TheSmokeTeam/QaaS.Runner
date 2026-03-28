internal static class SweepContextHelpers
{
    public static Dictionary<string, object?> CreateSupportRunnerDataSource(HookInventory hooks, SupportPaths support,
        string name)
    {
        return new Dictionary<string, object?>
        {
            ["Name"] = name == "support-generator" ? support.SupportDataSourceName : name,
            ["Generator"] = hooks.Generators.Default.FullName,
            ["GeneratorConfiguration"] = BuildHookBaseline(hooks.Generators.Default.ConfigurationType!, support)
        };
    }

    public static Dictionary<string, object?> CreateSupportMockerDataSource(HookInventory hooks, SupportPaths support)
    {
        return new Dictionary<string, object?>
        {
            ["Name"] = support.SupportDataSourceName,
            ["Generator"] = hooks.Generators.Default.FullName,
            ["GeneratorConfiguration"] = BuildHookBaseline(hooks.Generators.Default.ConfigurationType!, support)
        };
    }

    public static Dictionary<string, object?> CreateSupportMockerStub(HookInventory hooks, SupportPaths support)
    {
        return new Dictionary<string, object?>
        {
            ["Name"] = support.SupportStubName,
            ["Processor"] = hooks.Processors.Default.FullName,
            ["ProcessorConfiguration"] = BuildHookBaseline(hooks.Processors.Default.ConfigurationType!, support)
        };
    }

    public static Dictionary<string, object?> CreateSupportMockerServer(SupportPaths support, string? stubName = null)
    {
        return new Dictionary<string, object?>
        {
            ["Http"] = new Dictionary<string, object?>
            {
                ["Port"] = 8080,
                ["Endpoints"] = new List<object?>
                {
                    new Dictionary<string, object?>
                    {
                        ["Path"] = "/support/{id}",
                        ["Actions"] = new List<object?>
                        {
                            new Dictionary<string, object?>
                            {
                                ["Name"] = "support-action",
                                ["Method"] = "Get",
                                ["TransactionStubName"] = stubName ?? support.SupportStubName
                            }
                        }
                    }
                }
            }
        };
    }

    public static object? BuildHookBaseline(Type configurationType, SupportPaths support)
    {
        return SweepNodes.BuildMinimalNode(
            configurationType,
            new ContextSpec(
                "hook",
                "hook",
                configurationType,
                ValidationKind.RunnerTemplate,
                "hook",
                "yaml",
                (_, _, _) => new Dictionary<string, object?>()),
            support,
            false);
    }
}
