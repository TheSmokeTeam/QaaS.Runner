using QaaS.Framework.Policies;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.ConfigurationObjects;
using QaaS.Runner.Sessions.Actions.Collectors;
using QaaS.Runner.Sessions.Actions.Consumers.Builders;
using QaaS.Runner.Sessions.Actions.MockerCommands;
using QaaS.Runner.Sessions.Actions.Probes;
using QaaS.Runner.Sessions.Actions.Publishers.Builders;
using QaaS.Runner.Sessions.Actions.Transactions.Builders;
using QaaS.Runner.Sessions.ConfigurationObjects;
using QaaS.Runner.Sessions.Session.Builders;
using QaaS.Runner.Storage;

internal static class SweepRunnerContexts
{
    public static IEnumerable<ContextSpec> Build(HookInventory hooks, SupportPaths support)
    {
        var contexts = new List<ContextSpec>
        {
            new(
                "runner",
                "Runner.MetaData",
                typeof(MetaDataConfig),
                ValidationKind.RunnerTemplate,
                "runner-metadata",
                "qaas.yaml",
                CreateRoot: (target, _, _) => new Dictionary<string, object?> { ["MetaData"] = target },
                BaselineOverrides: new Dictionary<string, object?>
                {
                    ["Team"] = "SweepTeam",
                    ["System"] = "SweepSystem"
                }),
            new(
                "runner",
                "Runner.DataSource.Common",
                typeof(DataSourceBuilder),
                ValidationKind.RunnerTemplate,
                "runner-datasource-common",
                "qaas.yaml",
                CreateRoot: (target, inventory, paths) => new Dictionary<string, object?>
                {
                    ["DataSources"] = new List<object?>
                    {
                        SweepContextHelpers.CreateSupportRunnerDataSource(inventory, paths, "support-generator"),
                        target
                    }
                },
                TerminalProperties: ["GeneratorConfiguration"],
                BaselineOverrides: new Dictionary<string, object?>
                {
                    ["Name"] = "target-data-source",
                    ["Generator"] = hooks.Generators.Default.FullName,
                    ["GeneratorConfiguration"] = SweepContextHelpers.BuildHookBaseline(hooks.Generators.Default.ConfigurationType!, support)
                },
                FilledRemovals: ["Deserialize"]),
            new(
                "runner",
                "Runner.Session.Common",
                typeof(SessionBuilder),
                ValidationKind.RunnerTemplate,
                "runner-session-common",
                "qaas.yaml",
                CreateRoot: (target, _, _) => new Dictionary<string, object?> { ["Sessions"] = new List<object?> { target } },
                TerminalProperties:
                [
                    "Consumers", "Publishers", "Transactions", "Probes", "Collectors", "MockerCommands", "Stages"
                ],
                BaselineOverrides: new Dictionary<string, object?>
                {
                    ["Name"] = support.SupportSessionName
                },
                FilledOverrides: new Dictionary<string, object?>
                {
                    ["Consumers[0].Name"] = "consumer-1",
                    ["Consumers[0].TimeoutMs"] = 1000,
                    ["Consumers[0].RabbitMq.QueueName"] = "queue-1",
                    ["Publishers[0].Name"] = "publisher-1",
                    ["Publishers[0].DataSourceNames[0]"] = support.SupportDataSourceName,
                    ["Publishers[0].RabbitMq.QueueName"] = "queue-1",
                    ["Transactions[0].Name"] = "transaction-1",
                    ["Transactions[0].TimeoutMs"] = 1000,
                    ["Transactions[0].DataSourceNames[0]"] = support.SupportDataSourceName,
                    ["Transactions[0].Http.BaseAddress"] = "http://localhost",
                    ["Probes[0].Name"] = "probe-1",
                    ["Probes[0].Probe"] = hooks.Probes.Default.FullName,
                    ["Probes[0].ProbeConfiguration"] = SweepContextHelpers.BuildHookBaseline(hooks.Probes.Default.ConfigurationType!, support),
                    ["Collectors[0].Name"] = "collector-1",
                    ["MockerCommands[0].Name"] = "mocker-command-1",
                    ["MockerCommands[0].ServerName"] = "controller-server",
                    ["MockerCommands[0].Command"] = SweepNodes.BuildMinimalNode(typeof(MockerCommandConfig), new ContextSpec(
                        "runner", "temp", typeof(MockerCommandConfig), ValidationKind.RunnerTemplate, "temp", "yaml",
                        (_, _, _) => new Dictionary<string, object?>(),
                        SelectedVariants: new Dictionary<Type, string> { [typeof(MockerCommandConfig)] = "ChangeActionStub" }), support, true),
                    ["Stages[0].StageNumber"] = 1
                },
                FilledRemovals:
                [
                    "Consumers[0].RabbitMq.ExchangeName",
                    "Publishers[0].RabbitMq.ExchangeName",
                    "Transactions[0].DataSourcePatterns"
                ]),
            new(
                "runner",
                "Runner.Probe.Common",
                typeof(ProbeBuilder),
                ValidationKind.RunnerTemplate,
                "runner-probe-common",
                "qaas.yaml",
                CreateRoot: (target, inventory, paths) => new Dictionary<string, object?>
                {
                    ["DataSources"] = new List<object?> { SweepContextHelpers.CreateSupportRunnerDataSource(inventory, paths, "support-generator") },
                    ["Sessions"] = new List<object?>
                    {
                        new Dictionary<string, object?>
                        {
                            ["Name"] = paths.SupportSessionName,
                            ["Probes"] = new List<object?> { target }
                        }
                    }
                },
                TerminalProperties: ["ProbeConfiguration"],
                BaselineOverrides: new Dictionary<string, object?>
                {
                    ["Name"] = "probe-1",
                    ["Probe"] = hooks.Probes.Default.FullName,
                    ["ProbeConfiguration"] = SweepContextHelpers.BuildHookBaseline(hooks.Probes.Default.ConfigurationType!, support)
                }),
            new(
                "runner",
                "Runner.Assertion.Common",
                typeof(AssertionBuilder),
                ValidationKind.RunnerTemplate,
                "runner-assertion-common",
                "qaas.yaml",
                CreateRoot: (target, inventory, paths) => new Dictionary<string, object?>
                {
                    ["DataSources"] = new List<object?> { SweepContextHelpers.CreateSupportRunnerDataSource(inventory, paths, "support-generator") },
                    ["Sessions"] = new List<object?>
                    {
                        new Dictionary<string, object?> { ["Name"] = paths.SupportSessionName }
                    },
                    ["Assertions"] = new List<object?> { target }
                },
                TerminalProperties: ["AssertionConfiguration"],
                BaselineOverrides: new Dictionary<string, object?>
                {
                    ["Assertion"] = hooks.Assertions.Default.FullName,
                    ["Name"] = "assertion-1",
                    ["SessionNames"] = new List<object?> { support.SupportSessionName },
                    ["AssertionConfiguration"] = SweepContextHelpers.BuildHookBaseline(hooks.Assertions.Default.ConfigurationType!, support)
                }),
            new(
                "runner",
                "Runner.Stage",
                typeof(StageConfig),
                ValidationKind.RunnerTemplate,
                "runner-stage",
                "qaas.yaml",
                CreateRoot: (target, _, paths) => new Dictionary<string, object?>
                {
                    ["Sessions"] = new List<object?>
                    {
                        new Dictionary<string, object?>
                        {
                            ["Name"] = paths.SupportSessionName,
                            ["Stages"] = new List<object?> { target }
                        }
                    }
                },
                BaselineOverrides: new Dictionary<string, object?>
                {
                    ["StageNumber"] = 1
                }),
            new(
                "runner",
                "Runner.Execute",
                typeof(ExecuteConfigurations),
                ValidationKind.RunnerExecute,
                "runner-execute",
                "yaml",
                CreateRoot: (target, _, _) => (Dictionary<string, object?>)target!,
                BaselineOverrides: new Dictionary<string, object?>
                {
                    ["Commands[0].Command"] = $"template {support.RunnerExecuteInnerPath} --no-process-exit --no-env",
                    ["Commands[0].Id"] = "cmd1"
                })
        };

        contexts.AddRange(BuildStorageAndLinkContexts());
        contexts.AddRange(BuildPolicyContexts(support));
        contexts.AddRange(BuildActionContexts(hooks, support));
        contexts.AddRange(BuildHookContexts(hooks, support));

        return contexts;
    }

    private static IEnumerable<ContextSpec> BuildStorageAndLinkContexts()
    {
        yield return new ContextSpec(
            "runner",
            "Runner.Storage.FileSystem",
            typeof(StorageBuilder),
            ValidationKind.RunnerTemplate,
            "runner-storage-filesystem",
            "qaas.yaml",
            CreateRoot: (target, _, _) => new Dictionary<string, object?> { ["Storages"] = new List<object?> { target } },
            SelectedVariants: new Dictionary<Type, string> { [typeof(StorageBuilder)] = "FileSystem" });

        yield return new ContextSpec(
            "runner",
            "Runner.Storage.S3",
            typeof(StorageBuilder),
            ValidationKind.RunnerTemplate,
            "runner-storage-s3",
            "qaas.yaml",
            CreateRoot: (target, _, _) => new Dictionary<string, object?> { ["Storages"] = new List<object?> { target } },
            SelectedVariants: new Dictionary<Type, string> { [typeof(StorageBuilder)] = "S3" });

        foreach (var variant in new[] { "Kibana", "Prometheus", "Grafana" })
        {
            yield return new ContextSpec(
                "runner",
                $"Runner.Link.{variant}",
                typeof(LinkBuilder),
                ValidationKind.RunnerTemplate,
                $"runner-link-{variant.ToLowerInvariant()}",
                "qaas.yaml",
                CreateRoot: (target, _, _) => new Dictionary<string, object?> { ["Links"] = new List<object?> { target } },
                SelectedVariants: new Dictionary<Type, string> { [typeof(LinkBuilder)] = variant });
        }
    }

    private static IEnumerable<ContextSpec> BuildPolicyContexts(SupportPaths support)
    {
        foreach (var variant in new[] { "Count", "Timeout", "LoadBalance", "IncreasingLoadBalance", "AdvancedLoadBalance" })
        {
            var baselineOverrides = new Dictionary<string, object?>();
            if (variant == "AdvancedLoadBalance")
            {
                baselineOverrides["AdvancedLoadBalance.Stages[0].TimeoutMs"] = 1000;
                baselineOverrides["AdvancedLoadBalance.Stages[0].Rate"] = 1;
            }

            yield return new ContextSpec(
                "runner",
                $"Runner.Policy.{variant}",
                typeof(PolicyBuilder),
                ValidationKind.RunnerTemplate,
                $"runner-policy-{variant.ToLowerInvariant()}",
                "qaas.yaml",
                CreateRoot: (target, _, paths) => new Dictionary<string, object?>
                {
                    ["Sessions"] = new List<object?>
                    {
                        new Dictionary<string, object?>
                        {
                            ["Name"] = paths.SupportSessionName,
                            ["Publishers"] = new List<object?>
                            {
                                new Dictionary<string, object?>
                                {
                                    ["Name"] = "publisher-1",
                                    ["DataSourceNames"] = new List<object?> { paths.SupportDataSourceName },
                                    ["RabbitMq"] = new Dictionary<string, object?>(),
                                    ["Policies"] = new List<object?> { target }
                                }
                            }
                        }
                    }
                },
                SelectedVariants: new Dictionary<Type, string>
                {
                    [typeof(PolicyBuilder)] = variant,
                    [typeof(PublisherBuilder)] = "RabbitMq"
                },
                BaselineOverrides: baselineOverrides);
        }
    }

    private static IEnumerable<ContextSpec> BuildActionContexts(HookInventory hooks, SupportPaths support)
    {
        foreach (var variant in new[]
                 {
                     "RabbitMq", "KafkaTopic", "Redis", "PostgreSqlTable", "OracleSqlTable", "S3Bucket", "Socket",
                     "ElasticIndex", "MsSqlTable", "MongoDbCollection", "Sftp"
                 })
        {
            var baselineOverrides = new Dictionary<string, object?>
            {
                ["Name"] = "publisher-1",
                ["DataSourceNames"] = new List<object?> { support.SupportDataSourceName }
            };
            if (variant == "RabbitMq")
            {
                baselineOverrides["RabbitMq.QueueName"] = "queue-1";
            }
            else if (variant is "Redis" or "OracleSqlTable" or "ElasticIndex" or "MsSqlTable" or "MongoDbCollection")
            {
                baselineOverrides["Chunk.ChunkSize"] = 1;
            }

            yield return BuildSessionActionVariant(typeof(PublisherBuilder), $"Runner.Publisher.{variant}", variant,
                $"runner-publisher-{variant.ToLowerInvariant()}", support, hooks, "Publishers", baselineOverrides,
                requiredOnlyRemovals: variant == "RabbitMq" ? ["RabbitMq.ExchangeName"] : [],
                filledRemovals: variant switch
                {
                    "RabbitMq" => ["RabbitMq.ExchangeName", "Chunk"],
                    "KafkaTopic" => ["Chunk"],
                    "S3Bucket" => ["Chunk"],
                    "Socket" => ["Chunk"],
                    "Sftp" => ["Chunk"],
                    _ => []
                });
        }

        foreach (var variant in new[]
                 {
                     "RabbitMq", "KafkaTopic", "MsSqlTable", "OracleSqlTable", "TrinoSqlTable", "PostgreSqlTable",
                     "S3Bucket", "ElasticIndices", "Socket", "IbmMqQueue"
                 })
        {
            var baselineOverrides = new Dictionary<string, object?>
            {
                ["Name"] = "consumer-1",
                ["TimeoutMs"] = 1000
            };
            if (variant == "RabbitMq")
            {
                baselineOverrides["RabbitMq.QueueName"] = "queue-1";
            }

            yield return BuildSessionActionVariant(typeof(ConsumerBuilder), $"Runner.Consumer.{variant}", variant,
                $"runner-consumer-{variant.ToLowerInvariant()}", support, hooks, "Consumers", baselineOverrides,
                requiredOnlyRemovals: variant == "RabbitMq" ? ["RabbitMq.ExchangeName"] : [],
                filledRemovals: variant == "RabbitMq" ? ["RabbitMq.ExchangeName"] : []);
        }

        foreach (var variant in new[] { "Http", "Grpc" })
        {
            var baselineOverrides = new Dictionary<string, object?>
            {
                ["Name"] = "transaction-1",
                ["TimeoutMs"] = 1000,
                ["DataSourceNames"] = new List<object?> { support.SupportDataSourceName }
            };
            if (variant == "Http")
            {
                baselineOverrides["Http.BaseAddress"] = "http://localhost";
            }
            else
            {
                baselineOverrides["Grpc.Port"] = 5001;
            }

            yield return BuildSessionActionVariant(typeof(TransactionBuilder), $"Runner.Transaction.{variant}", variant,
                $"runner-transaction-{variant.ToLowerInvariant()}", support, hooks, "Transactions", baselineOverrides,
                filledRemovals: variant == "Http" ? ["Http.JwtAuth.HierarchicalClaims"] : []);
        }
    }

    private static IEnumerable<ContextSpec> BuildHookContexts(HookInventory hooks, SupportPaths support)
    {
        yield return new ContextSpec(
            "runner",
            "Runner.Collector.Prometheus",
            typeof(CollectorBuilder),
            ValidationKind.RunnerTemplate,
            "runner-collector-prometheus",
            "qaas.yaml",
            CreateRoot: (target, _, paths) => new Dictionary<string, object?>
            {
                ["Sessions"] = new List<object?>
                {
                    new Dictionary<string, object?>
                    {
                        ["Name"] = paths.SupportSessionName,
                        ["Collectors"] = new List<object?> { target }
                    }
                }
            },
            SelectedVariants: new Dictionary<Type, string> { [typeof(CollectorBuilder)] = "Prometheus" },
            BaselineOverrides: new Dictionary<string, object?>
            {
                ["Name"] = "collector-1"
            });

        foreach (var variant in new[] { "ChangeActionStub", "TriggerAction", "Consume" })
        {
            yield return new ContextSpec(
                "runner",
                $"Runner.MockerCommand.{variant}",
                typeof(MockerCommandBuilder),
                ValidationKind.RunnerTemplate,
                $"runner-mocker-command-{variant.ToLowerInvariant()}",
                "qaas.yaml",
                CreateRoot: (target, _, paths) => new Dictionary<string, object?>
                {
                    ["Sessions"] = new List<object?>
                    {
                        new Dictionary<string, object?>
                        {
                            ["Name"] = paths.SupportSessionName,
                            ["MockerCommands"] = new List<object?> { target }
                        }
                    }
                },
                SelectedVariants: new Dictionary<Type, string> { [typeof(MockerCommandConfig)] = variant },
                BaselineOverrides: new Dictionary<string, object?>
                {
                    ["Name"] = "mocker-command-1",
                    ["ServerName"] = "controller-server",
                    ["Command"] = SweepNodes.BuildMinimalNode(typeof(MockerCommandConfig), new ContextSpec(
                        "runner", "temp", typeof(MockerCommandConfig), ValidationKind.RunnerTemplate, "temp", "yaml",
                        (_, _, _) => new Dictionary<string, object?>(),
                        SelectedVariants: new Dictionary<Type, string> { [typeof(MockerCommandConfig)] = variant }), support, true)
                });
        }

        foreach (var hook in hooks.Generators.Hooks)
        {
            yield return new ContextSpec(
                "runner",
                $"Runner.GeneratorHook.{hook.DisplayName}",
                hook.ConfigurationType!,
                ValidationKind.RunnerTemplate,
                $"runner-generator-hook-{hook.DisplayName.ToLowerInvariant()}",
                "qaas.yaml",
                CreateRoot: (target, inventory, paths) => new Dictionary<string, object?>
                {
                    ["DataSources"] = new List<object?>
                    {
                        SweepContextHelpers.CreateSupportRunnerDataSource(inventory, paths, "support-generator"),
                        new Dictionary<string, object?>
                        {
                            ["Name"] = "target-data-source",
                            ["Generator"] = hook.FullName,
                            ["GeneratorConfiguration"] = target
                        }
                    }
                });
        }

        foreach (var hook in hooks.Probes.Hooks)
        {
            yield return new ContextSpec(
                "runner",
                $"Runner.ProbeHook.{hook.DisplayName}",
                hook.ConfigurationType!,
                ValidationKind.RunnerTemplate,
                $"runner-probe-hook-{hook.DisplayName.ToLowerInvariant()}",
                "qaas.yaml",
                CreateRoot: (target, inventory, paths) => new Dictionary<string, object?>
                {
                    ["DataSources"] = new List<object?> { SweepContextHelpers.CreateSupportRunnerDataSource(inventory, paths, "support-generator") },
                    ["Sessions"] = new List<object?>
                    {
                        new Dictionary<string, object?>
                        {
                            ["Name"] = paths.SupportSessionName,
                            ["Probes"] = new List<object?>
                            {
                                new Dictionary<string, object?>
                                {
                                    ["Name"] = "probe-1",
                                    ["Probe"] = hook.FullName,
                                    ["ProbeConfiguration"] = target
                                }
                            }
                        }
                    }
                });
        }

        foreach (var hook in hooks.Assertions.Hooks)
        {
            yield return new ContextSpec(
                "runner",
                $"Runner.AssertionHook.{hook.DisplayName}",
                hook.ConfigurationType!,
                ValidationKind.RunnerTemplate,
                $"runner-assertion-hook-{hook.DisplayName.ToLowerInvariant()}",
                "qaas.yaml",
                CreateRoot: (target, inventory, paths) => new Dictionary<string, object?>
                {
                    ["DataSources"] = new List<object?> { SweepContextHelpers.CreateSupportRunnerDataSource(inventory, paths, "support-generator") },
                    ["Sessions"] = new List<object?>
                    {
                        new Dictionary<string, object?> { ["Name"] = paths.SupportSessionName }
                    },
                    ["Assertions"] = new List<object?>
                    {
                        new Dictionary<string, object?>
                        {
                            ["Assertion"] = hook.FullName,
                            ["Name"] = "assertion-1",
                            ["SessionNames"] = new List<object?> { paths.SupportSessionName },
                            ["AssertionConfiguration"] = target
                        }
                    }
                });
        }
    }

    private static ContextSpec BuildSessionActionVariant(
        Type targetType,
        string name,
        string variant,
        string fileStem,
        SupportPaths support,
        HookInventory hooks,
        string collectionName,
        IReadOnlyDictionary<string, object?> baselineOverrides,
        IReadOnlyCollection<string>? requiredOnlyRemovals = null,
        IReadOnlyCollection<string>? filledRemovals = null)
    {
        return new ContextSpec(
            "runner",
            name,
            targetType,
            ValidationKind.RunnerTemplate,
            fileStem,
            "qaas.yaml",
            CreateRoot: (target, inventory, paths) => new Dictionary<string, object?>
            {
                ["DataSources"] = new List<object?> { SweepContextHelpers.CreateSupportRunnerDataSource(inventory, paths, "support-generator") },
                ["Sessions"] = new List<object?>
                {
                    new Dictionary<string, object?>
                    {
                        ["Name"] = paths.SupportSessionName,
                        [collectionName] = new List<object?> { target }
                    }
                }
            },
            SelectedVariants: new Dictionary<Type, string>
            {
                [targetType] = variant,
                [typeof(PolicyBuilder)] = "Count"
            },
            BaselineOverrides: baselineOverrides,
            RequiredOnlyRemovals: requiredOnlyRemovals,
            FilledRemovals: filledRemovals);
    }
}
