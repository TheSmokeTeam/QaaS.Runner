using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Mocker.Controller.ConfigurationObjects;
using QaaS.Mocker.Servers.ConfigurationObjects;
using QaaS.Mocker.Stubs.ConfigurationObjects;

internal static class SweepMockerContexts
{
    public static IEnumerable<ContextSpec> Build(HookInventory hooks, SupportPaths support)
    {
        var contexts = new List<ContextSpec>
        {
            new(
                "mocker",
                "Mocker.DataSource.Common",
                typeof(DataSourceBuilder),
                ValidationKind.MockerTemplate,
                "mocker-datasource-common",
                "qaas.yaml",
                CreateRoot: (target, inventory, paths) => new Dictionary<string, object?>
                {
                    ["DataSources"] = new List<object?>
                    {
                        SweepContextHelpers.CreateSupportMockerDataSource(inventory, paths),
                        target
                    },
                    ["Stubs"] = new List<object?> { SweepContextHelpers.CreateSupportMockerStub(inventory, paths) },
                    ["Server"] = SweepContextHelpers.CreateSupportMockerServer(paths)
                },
                TerminalProperties: ["GeneratorConfiguration"],
                BaselineOverrides: new Dictionary<string, object?>
                {
                    ["Name"] = "target-data-source",
                    ["Generator"] = hooks.Generators.Default.FullName,
                    ["GeneratorConfiguration"] = SweepContextHelpers.BuildHookBaseline(hooks.Generators.Default.ConfigurationType!, support)
                }),
            new(
                "mocker",
                "Mocker.Stub.Common",
                typeof(TransactionStubConfig),
                ValidationKind.MockerTemplate,
                "mocker-stub-common",
                "qaas.yaml",
                CreateRoot: (target, _, paths) => new Dictionary<string, object?>
                {
                    ["Stubs"] = new List<object?> { target },
                    ["Server"] = SweepContextHelpers.CreateSupportMockerServer(paths, "target-stub")
                },
                AlwaysIncludeRootProperties: ["RequestBodyDeserialization", "ResponseBodySerialization"],
                TerminalProperties: ["ProcessorConfiguration"],
                BaselineOverrides: new Dictionary<string, object?>
                {
                    ["Name"] = "target-stub",
                    ["Processor"] = hooks.Processors.Default.FullName,
                    ["ProcessorConfiguration"] = SweepContextHelpers.BuildHookBaseline(hooks.Processors.Default.ConfigurationType!, support)
                }),
            new(
                "mocker",
                "Mocker.Controller",
                typeof(ControllerConfig),
                ValidationKind.MockerTemplate,
                "mocker-controller",
                "qaas.yaml",
                CreateRoot: (target, inventory, paths) => new Dictionary<string, object?>
                {
                    ["Stubs"] = new List<object?> { SweepContextHelpers.CreateSupportMockerStub(inventory, paths) },
                    ["Server"] = SweepContextHelpers.CreateSupportMockerServer(paths),
                    ["Controller"] = target
                },
                AlwaysIncludeRootProperties: ["Redis"],
                BaselineOverrides: new Dictionary<string, object?>
                {
                    ["ServerName"] = "controller-server"
                })
        };

        foreach (var variant in new[] { "Http", "Grpc", "Socket" })
        {
            contexts.Add(new ContextSpec(
                "mocker",
                $"Mocker.Server.{variant}",
                typeof(ServerConfig),
                ValidationKind.MockerTemplate,
                $"mocker-server-{variant.ToLowerInvariant()}",
                "qaas.yaml",
                CreateRoot: (target, inventory, paths) => new Dictionary<string, object?>
                {
                    ["Stubs"] = new List<object?> { SweepContextHelpers.CreateSupportMockerStub(inventory, paths) },
                    ["Server"] = target
                },
                SelectedVariants: new Dictionary<Type, string> { [typeof(ServerConfig)] = variant }));
        }

        foreach (var hook in hooks.Processors.Hooks)
        {
            contexts.Add(new ContextSpec(
                "mocker",
                $"Mocker.ProcessorHook.{hook.DisplayName}",
                hook.ConfigurationType!,
                ValidationKind.MockerTemplate,
                $"mocker-processor-hook-{hook.DisplayName.ToLowerInvariant()}",
                "qaas.yaml",
                CreateRoot: (target, _, paths) => new Dictionary<string, object?>
                {
                    ["Stubs"] = new List<object?>
                    {
                        new Dictionary<string, object?>
                        {
                            ["Name"] = "target-stub",
                            ["Processor"] = hook.FullName,
                            ["ProcessorConfiguration"] = target
                        }
                    },
                    ["Server"] = SweepContextHelpers.CreateSupportMockerServer(paths, "target-stub")
                }));
        }

        foreach (var hook in hooks.Generators.Hooks)
        {
            contexts.Add(new ContextSpec(
                "mocker",
                $"Mocker.GeneratorHook.{hook.DisplayName}",
                hook.ConfigurationType!,
                ValidationKind.MockerTemplate,
                $"mocker-generator-hook-{hook.DisplayName.ToLowerInvariant()}",
                "qaas.yaml",
                CreateRoot: (target, inventory, paths) => new Dictionary<string, object?>
                {
                    ["DataSources"] = new List<object?>
                    {
                        SweepContextHelpers.CreateSupportMockerDataSource(inventory, paths),
                        new Dictionary<string, object?>
                        {
                            ["Name"] = "target-data-source",
                            ["Generator"] = hook.FullName,
                            ["GeneratorConfiguration"] = target
                        }
                    },
                    ["Stubs"] = new List<object?> { SweepContextHelpers.CreateSupportMockerStub(inventory, paths) },
                    ["Server"] = SweepContextHelpers.CreateSupportMockerServer(paths)
                }));
        }

        return contexts;
    }
}
