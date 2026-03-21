using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace QaaS.Runner.Infrastructure.Tests;

[TestFixture]
public class ConfigurationTemplateRendererTests
{
    private sealed class DefaultValueSection
    {
        [System.ComponentModel.DefaultValue(5)]
        public int Count { get; set; } = 5;
    }

    private sealed class SerializationShape
    {
        public string Name { get; set; } = "keep";
        public string Blank { get; set; } = string.Empty;
        public Func<int> Callback { get; set; } = () => 1;
        public static string StaticValue { get; set; } = "skip";
        public string this[int index] => $"value-{index}";
    }

    private sealed class NullableDefaultSection
    {
        [System.ComponentModel.DefaultValue(null)]
        public string? Value { get; set; }
    }

    private sealed class NoDefaultValueSection
    {
        public int Count { get; set; }
    }

    private sealed record RecordShape(string Name);

    [Test]
    public void Render_UsesMergedConfigurationValuesAndAugmentsAssertionStatuses()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storages:0:FileSystem:Path"] = "SessionDataStorage",
                ["DataSources:0:Name"] = "RabbitPayload",
                ["DataSources:0:Generator"] = "TestGenerator",
                ["DataSources:0:GeneratorConfiguration:Count"] = "1",
                ["Sessions:0:Name"] = "RabbitRoundTrip",
                ["Sessions:0:SaveData"] = "true",
                ["Sessions:0:Publishers:0:Name"] = "PublishToRabbit",
                ["Sessions:0:Publishers:0:DataSourceNames:0"] = "RabbitPayload",
                ["Sessions:0:Publishers:0:RabbitMq:Host"] = "localhost",
                ["Sessions:0:Publishers:0:RabbitMq:Port"] = "5672",
                ["Sessions:0:Publishers:0:RabbitMq:RoutingKey"] = "/",
                ["Sessions:0:Publishers:0:RabbitMq:ExchangeName"] = "test",
                ["Sessions:0:Consumers:0:Name"] = "ConsumeFromRabbit",
                ["Sessions:0:Consumers:0:TimeoutMs"] = "20000",
                ["Sessions:0:Consumers:0:RabbitMq:Host"] = "localhost",
                ["Sessions:0:Consumers:0:RabbitMq:Port"] = "5672",
                ["Sessions:0:Consumers:0:RabbitMq:RoutingKey"] = "/",
                ["Sessions:0:Consumers:0:RabbitMq:ExchangeName"] = "test",
                ["Assertions:0:Name"] = "RabbitRoundTripAssertion",
                ["Assertions:0:Assertion"] = "RabbitRoundTripAssertion",
                ["Assertions:0:SessionNames:0"] = "RabbitRoundTrip",
                ["MetaData:System"] = "QaaS",
                ["MetaData:Team"] = "Smoke"
            })
            .Build();

        var yaml = ConfigurationTemplateRenderer.Render(
            configuration,
            sectionOrder: Constants.ConfigurationSectionNames,
            includedSessionNames: new HashSet<string>(["RabbitRoundTrip"], StringComparer.Ordinal),
            assertionStatusesToReport: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["RabbitRoundTripAssertion"] = ["Passed", "Failed", "Broken", "Unknown", "Skipped"]
            });

        Assert.That(yaml, Does.Contain("Storages:"));
        Assert.That(yaml, Does.Contain("Path: SessionDataStorage"));
        Assert.That(yaml, Does.Contain("GeneratorConfiguration:"));
        Assert.That(yaml, Does.Contain("Count: 1"));
        Assert.That(yaml, Does.Contain("SaveData: true"));
        Assert.That(yaml, Does.Contain("Port: 5672"));
        Assert.That(yaml, Does.Contain("RoutingKey: /"));
        Assert.That(yaml, Does.Contain("StatusesToReport:"));
        Assert.That(yaml, Does.Contain("- Passed"));
        Assert.That(yaml, Does.Not.Contain("CreatedQueueTimeToExpireMs"));
    }

    [Test]
    public void Render_FiltersSessionsAndAssertionsToTheSelectedRuntimeSet()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sessions:0:Name"] = "RabbitRoundTrip",
                ["Sessions:1:Name"] = "ShouldBeFilteredOut",
                ["Assertions:0:Name"] = "RabbitRoundTripAssertion",
                ["Assertions:0:Assertion"] = "RabbitRoundTripAssertion",
                ["Assertions:1:Name"] = "FilteredAssertion",
                ["Assertions:1:Assertion"] = "FilteredAssertion"
            })
            .Build();

        var yaml = ConfigurationTemplateRenderer.Render(
            configuration,
            sectionOrder: Constants.ConfigurationSectionNames,
            includedSessionNames: new HashSet<string>(["RabbitRoundTrip"], StringComparer.Ordinal),
            assertionStatusesToReport: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["RabbitRoundTripAssertion"] = ["Passed"]
            });

        Assert.That(yaml, Does.Contain("RabbitRoundTrip"));
        Assert.That(yaml, Does.Contain("RabbitRoundTripAssertion"));
        Assert.That(yaml, Does.Not.Contain("ShouldBeFilteredOut"));
        Assert.That(yaml, Does.Not.Contain("FilteredAssertion"));
    }

    [Test]
    public void Render_UsesStorageAliasWhenSourceConfigurationUsesLegacySectionName()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:0:FileSystem:Path"] = "LegacyStorage"
            })
            .Build();

        var yaml = ConfigurationTemplateRenderer.Render(configuration);

        Assert.That(yaml, Does.Contain("Storages:"));
        Assert.That(yaml, Does.Contain("LegacyStorage"));
    }

    [Test]
    public void Render_PreservesExplicitlyConfiguredDefaultValues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MetaData:Count"] = "5"
            })
            .Build();

        var yaml = ConfigurationTemplateRenderer.Render(configuration,
            fallbackSections:
            [
                new KeyValuePair<string, object?>("MetaData", new DefaultValueSection())
            ],
            sectionOrder: ["MetaData"]);

        Assert.That(yaml, Does.Contain("Count: 5"));
    }

    [Test]
    public void Render_OmitsImplicitDefaultValuesThatWereNotConfigured()
    {
        var yaml = ConfigurationTemplateRenderer.Render(new ConfigurationBuilder().Build(),
            fallbackSections:
            [
                new KeyValuePair<string, object?>("MetaData", new DefaultValueSection())
            ],
            sectionOrder: ["MetaData"]);

        Assert.That(yaml, Does.Not.Contain("Count: 5"));
    }

    [Test]
    public void Render_FiltersItemsWithoutUsableNamesFromNamedSections()
    {
        var sessions = new object?[]
        {
            new Dictionary<string, object?> { ["Name"] = "KeepMe" },
            new Dictionary<string, object?> { ["Name"] = "DropMe" },
            new Dictionary<string, object?> { ["Name"] = " " },
            new Dictionary<string, object?> { ["Other"] = "MissingName" }
        };

        var yaml = ConfigurationTemplateRenderer.Render(new ConfigurationBuilder().Build(),
            fallbackSections:
            [
                new KeyValuePair<string, object?>("Sessions", sessions)
            ],
            sectionOrder: ["Sessions"],
            includedSessionNames: new HashSet<string>(["KeepMe"], StringComparer.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(yaml, Does.Contain("KeepMe"));
            Assert.That(yaml, Does.Not.Contain("DropMe"));
            Assert.That(yaml, Does.Not.Contain("MissingName"));
        });
    }

    [Test]
    public void Render_ReplacesExistingAssertionStatusesAndLeavesUnmappedAssertionsUntouched()
    {
        var assertions = new object?[]
        {
            new Dictionary<string, object?>
            {
                ["Name"] = "MappedAssertion",
                ["StatusesToReport"] = new[] { "Unknown" }
            },
            new Dictionary<string, object?>
            {
                ["Name"] = "UnmappedAssertion",
                ["StatusesToReport"] = new[] { "Failed" }
            }
        };

        var yaml = ConfigurationTemplateRenderer.Render(new ConfigurationBuilder().Build(),
            fallbackSections:
            [
                new KeyValuePair<string, object?>("Assertions", assertions)
            ],
            sectionOrder: ["Assertions"],
            assertionStatusesToReport: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["MappedAssertion"] = ["Passed", "Broken"],
                ["UnmappedAssertion"] = ["Failed"]
            });

        Assert.Multiple(() =>
        {
            Assert.That(yaml, Does.Contain("MappedAssertion"));
            Assert.That(yaml, Does.Contain("- Passed"));
            Assert.That(yaml, Does.Contain("- Broken"));
            Assert.That(yaml, Does.Contain("UnmappedAssertion"));
            Assert.That(yaml, Does.Contain("- Failed"));
            Assert.That(yaml, Does.Not.Contain("- Unknown"));
        });
    }

    [Test]
    public void Render_WithSparseSourceIndexes_PreservesOriginalIndexesWhenFallbackSectionsAreRendered()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sessions:10:Name"] = "SparseSession",
                ["Sessions:10:Publishers:10:Name"] = "SparsePublisher",
                ["Assertions:10:Name"] = "SparseAssertion",
                ["Assertions:10:Assertion"] = "SparseAssertion"
            })
            .Build();

        var yaml = ConfigurationTemplateRenderer.Render(
            configuration,
            fallbackSections:
            [
                new KeyValuePair<string, object?>("Sessions", new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["Name"] = "SparseSession",
                        ["Publishers"] = new object?[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["Name"] = "SparsePublisher"
                            }
                        }
                    }
                }),
                new KeyValuePair<string, object?>("Assertions", new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["Name"] = "SparseAssertion",
                        ["Assertion"] = "SparseAssertion"
                    }
                })
            ],
            sectionOrder: ["Sessions", "Assertions"],
            includedSessionNames: new HashSet<string>(["SparseSession"], StringComparer.Ordinal),
            assertionStatusesToReport: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["SparseAssertion"] = ["Passed"]
            });

        Assert.Multiple(() =>
        {
            Assert.That(yaml, Does.Contain("Sessions:"));
            Assert.That(yaml, Does.Contain("  10:"));
            Assert.That(yaml, Does.Contain("Name: SparseSession"));
            Assert.That(yaml, Does.Contain("Publishers:"));
            Assert.That(yaml, Does.Contain("SparsePublisher"));
            Assert.That(yaml, Does.Contain("Assertions:"));
            Assert.That(yaml, Does.Contain("Name: SparseAssertion"));
            Assert.That(yaml, Does.Contain("- Passed"));
            Assert.That(yaml, Does.Not.Contain("- Name: SparseSession"));
        });
    }

    [Test]
    public void NormalizeValue_DropsBlankDictionaryKeysAndNullEnumerableItems()
    {
        var normalized = (IDictionary<string, object?>)InvokePrivate("NormalizeValue", new Hashtable
        {
            ["Valid"] = "value",
            [" "] = "ignored",
            ["Items"] = new object?[] { null, "kept" }
        })!;

        Assert.Multiple(() =>
        {
            Assert.That(normalized.Keys, Is.EquivalentTo(new[] { "Valid", "Items" }));
            Assert.That((IList<object?>)normalized["Items"]!, Is.EqualTo(new object?[] { "kept" }));
        });
    }

    [Test]
    public void SerializeObject_SkipsIndexersStaticMembersDelegatesAndBlankStrings()
    {
        var serialized = (IDictionary<string, object?>)InvokePrivate("SerializeObject",
            new SerializationShape(),
            "Section",
            new HashSet<string>(StringComparer.Ordinal))!;

        Assert.That(serialized.Keys, Is.EqualTo(new[] { "Name" }));
        Assert.That(serialized["Name"], Is.EqualTo("keep"));
    }

    [Test]
    public void ShouldSkipValue_SkipsImplicitDefaultsButKeepsExplicitlyConfiguredDefaults()
    {
        var property = typeof(DefaultValueSection).GetProperty(nameof(DefaultValueSection.Count))!;

        var skippedImplicitly = (bool)InvokePrivate("ShouldSkipValue",
            property,
            5,
            "MetaData:Count",
            new HashSet<string>(StringComparer.Ordinal))!;
        var keptWhenConfigured = (bool)InvokePrivate("ShouldSkipValue",
            property,
            5,
            "MetaData:Count",
            new HashSet<string>(StringComparer.Ordinal) { "MetaData:Count" })!;

        Assert.Multiple(() =>
        {
            Assert.That(skippedImplicitly, Is.True);
            Assert.That(keptWhenConfigured, Is.False);
        });
    }

    [Test]
    public void FilterNamedSection_ReturnsOriginalValueWhenNamesAreNotProvidedOrValueIsNotAList()
    {
        var value = new Dictionary<string, object?> { ["Name"] = "ignored" };

        var withoutNames = InvokePrivate("FilterNamedSection", value, null);
        var nonListValue = InvokePrivate("FilterNamedSection", value, new HashSet<string>(["name"], StringComparer.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(withoutNames, Is.SameAs(value));
            Assert.That(nonListValue, Is.SameAs(value));
        });
    }

    [Test]
    public void AugmentAssertionStatuses_LeavesPlainItemsMissingNamesAndUnmappedAssertionsUntouched()
    {
        var originalList = new ArrayList
        {
            "plain-item",
            new Dictionary<string, object?> { ["Other"] = "MissingName" },
            new Dictionary<string, object?> { ["Name"] = "Unmapped", ["StatusesToReport"] = new[] { "Failed" } }
        };

        var updated = (IList)InvokePrivate("AugmentAssertionStatuses",
            originalList,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["Mapped"] = ["Passed"]
            })!;

        Assert.Multiple(() =>
        {
            Assert.That(updated[0], Is.EqualTo("plain-item"));
            Assert.That(updated[1], Is.InstanceOf<IDictionary<string, object?>>());
            Assert.That(updated[2], Is.InstanceOf<IDictionary<string, object?>>());

            var otherItem = (IDictionary<string, object?>)updated[1]!;
            var mappedItem = (IDictionary<string, object?>)updated[2]!;
            Assert.That(otherItem["Other"], Is.EqualTo("MissingName"));
            Assert.That(mappedItem["StatusesToReport"], Is.EqualTo(new[] { "Failed" }));
        });
    }

    [Test]
    public void SerializeEnumerable_DropsItemsThatNormalizeToNothing()
    {
        var serialized = (IList<object?>)InvokePrivate("SerializeEnumerable",
            new object?[] { null, Array.Empty<object>(), new Hashtable(), "kept" },
            "Section",
            new HashSet<string>(StringComparer.Ordinal))!;

        Assert.That(serialized, Is.EqualTo(new object?[] { "kept" }));
    }

    [Test]
    public void SerializeDictionary_DropsBlankKeysAndValuesThatShouldBeSkipped()
    {
        var serialized = (IDictionary<string, object?>)InvokePrivate("SerializeDictionary",
            new Hashtable
            {
                [" "] = "ignored",
                ["BlankValue"] = string.Empty,
                ["Valid"] = "kept"
            },
            "Section",
            new HashSet<string>(StringComparer.Ordinal))!;

        Assert.That(serialized.Keys, Is.EqualTo(new[] { "Valid" }));
        Assert.That(serialized["Valid"], Is.EqualTo("kept"));
    }

    [Test]
    public void ShouldSerializeProperty_ReturnsFalseForRecordEqualityContractAndTrueForRegularProperties()
    {
        var equalityContract = typeof(RecordShape).GetProperty("EqualityContract",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var regularProperty = typeof(RecordShape).GetProperty(nameof(RecordShape.Name))!;

        var shouldSerializeEqualityContract = (bool)InvokePrivate("ShouldSerializeProperty", equalityContract)!;
        var shouldSerializeRegularProperty = (bool)InvokePrivate("ShouldSerializeProperty", regularProperty)!;

        Assert.Multiple(() =>
        {
            Assert.That(shouldSerializeEqualityContract, Is.False);
            Assert.That(shouldSerializeRegularProperty, Is.True);
        });
    }

    [Test]
    public void IsDefaultValue_ReturnsFalseWhenAttributeIsMissingAndHandlesNullDefaults()
    {
        var withoutDefaultAttribute = typeof(NoDefaultValueSection).GetProperty(nameof(NoDefaultValueSection.Count))!;
        var nullDefaultProperty = typeof(NullableDefaultSection).GetProperty(nameof(NullableDefaultSection.Value))!;

        var missingAttributeResult = (bool)InvokePrivate("IsDefaultValue", withoutDefaultAttribute, 0)!;
        var nullDefaultResult = (bool)InvokePrivate("IsDefaultValue", nullDefaultProperty, null!)!;

        Assert.Multiple(() =>
        {
            Assert.That(missingAttributeResult, Is.False);
            Assert.That(nullDefaultResult, Is.True);
        });
    }

    [Test]
    public void TryGetName_ReturnsFalseForMissingOrBlankNames()
    {
        var missingName = (bool)InvokePrivate("TryGetName",
            new Hashtable { ["Other"] = "value" }, null!)!;
        var blankName = (bool)InvokePrivate("TryGetName",
            new Hashtable { ["Name"] = " " }, null!)!;

        Assert.Multiple(() =>
        {
            Assert.That(missingName, Is.False);
            Assert.That(blankName, Is.False);
        });
    }

    private static object? InvokePrivate(string methodName, params object?[] args)
    {
        return typeof(ConfigurationTemplateRenderer)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, args);
    }
}
