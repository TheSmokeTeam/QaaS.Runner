using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace QaaS.Runner.Infrastructure.Tests;

[TestFixture]
public class ConfigurationMutationExtensionsTests
{
    [Test]
    public void UpdateConfiguration_WithNullCurrentConfiguration_ReturnsIncomingConfiguration()
    {
        FirstConfig? current = null;

        var updated = current.UpdateConfiguration(new FirstConfig
        {
            Name = "incoming"
        });

        Assert.That(updated.Name, Is.EqualTo("incoming"));
    }

    [Test]
    public void UpdateConfiguration_WithNullIncomingConfiguration_ThrowsArgumentNullException()
    {
        var current = new FirstConfig();

        Assert.Throws<ArgumentNullException>(() => current.UpdateConfiguration<FirstConfig>(null!));
    }

    [Test]
    public void UpdateConfiguration_WithDifferentRuntimeTypes_ReplacesCurrentConfiguration()
    {
        ITestConfig current = new FirstConfig
        {
            Name = "before"
        };

        var updated = current.UpdateConfiguration<ITestConfig>(new SecondConfig
        {
            Count = 3
        });

        Assert.That(updated, Is.TypeOf<SecondConfig>());
        Assert.That(((SecondConfig)updated).Count, Is.EqualTo(3));
    }

    [Test]
    public void UpdateConfiguration_WithSparseSameTypeUpdate_PreservesExistingStringsAndNestedValues()
    {
        var current = new FirstConfig
        {
            Name = "existing",
            TimeoutSeconds = 30,
            Nested = new NestedConfig
            {
                Marker = "nested-existing"
            },
            Tags = ["one"]
        };

        var updated = current.UpdateConfiguration<ITestConfig>(new FirstConfig
        {
            TimeoutSeconds = 5,
            Nested = new NestedConfig
            {
                Marker = string.Empty
            }
        });

        var typedUpdated = (FirstConfig)updated;
        Assert.Multiple(() =>
        {
            Assert.That(typedUpdated.Name, Is.EqualTo("existing"));
            Assert.That(typedUpdated.TimeoutSeconds, Is.EqualTo(5));
            Assert.That(typedUpdated.Nested.Marker, Is.EqualTo("nested-existing"));
            Assert.That(typedUpdated.Tags, Is.EqualTo(new[] { "one" }));
        });
    }

    [Test]
    public void UpdateConfiguration_ForRawConfiguration_BindsOntoExistingTree()
    {
        var current = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Feature:Enabled"] = "true"
            })
            .Build();

        var updated = current.UpdateConfiguration(new
        {
            Feature = new
            {
                Threshold = 5
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(updated["Feature:Enabled"], Is.EqualTo("true"));
            Assert.That(updated["Feature:Threshold"], Is.EqualTo("5"));
        });
    }

    [Test]
    public void UpdateConfiguration_ForRawConfiguration_WithNullCurrentConfiguration_CreatesNewTree()
    {
        IConfiguration? current = null;

        var updated = current.UpdateConfiguration(new
        {
            Feature = new
            {
                Enabled = true
            }
        });

        Assert.That(updated["Feature:Enabled"], Is.EqualTo("True"));
    }

    [Test]
    public void UpdateConfiguration_WithNullCurrentNestedValue_ReplacesComplexProperty()
    {
        var current = new FirstConfig
        {
            Name = "existing",
            Nested = null!
        };

        var updated = current.UpdateConfiguration(new FirstConfig
        {
            Nested = new NestedConfig
            {
                Marker = "created"
            }
        });

        Assert.That(updated.Nested.Marker, Is.EqualTo("created"));
    }

    [Test]
    public void UpdateConfiguration_WithTypeWithoutDefaultConstructor_StillAppliesIncomingValues()
    {
        var current = new NoDefaultConfig("before")
        {
            Count = 1
        };

        var updated = current.UpdateConfiguration(new NoDefaultConfig("after")
        {
            Count = 2
        });

        Assert.Multiple(() =>
        {
            Assert.That(updated.Label, Is.EqualTo("after"));
            Assert.That(updated.Count, Is.EqualTo(2));
        });
    }

    [Test]
    public void UpdateConfiguration_WithEquivalentEnumerableValue_DoesNotReplaceExistingCollection()
    {
        var current = new FirstConfig
        {
            Name = "existing",
            Tags = ["one", "two"]
        };
        var originalTags = current.Tags;

        var updated = current.UpdateConfiguration(new FirstConfig
        {
            Tags = ["one", "two"]
        });

        Assert.That(updated.Tags, Is.EqualTo(originalTags));
    }

    [Test]
    public void UpdateConfiguration_WithDifferentEnumerableValue_ReplacesCollectionProperty()
    {
        var current = new FirstConfig
        {
            Tags = ["one"]
        };

        var updated = current.UpdateConfiguration(new FirstConfig
        {
            Tags = ["one", "two"]
        });

        Assert.That(updated.Tags, Is.EqualTo(new[] { "one", "two" }));
    }

    [Test]
    public void PrivateHelpers_CompareEnumerablesAndSkipIndexersAsExpected()
    {
        var getMergeableProperties = typeof(ConfigurationMutationExtensions)
            .GetMethod("GetMergeableProperties", BindingFlags.NonPublic | BindingFlags.Static)!;
        var areEquivalentEnumerables = typeof(ConfigurationMutationExtensions)
            .GetMethod("AreEquivalentEnumerables", BindingFlags.NonPublic | BindingFlags.Static)!;

        var mergeableProperties = ((IEnumerable<PropertyInfo>)getMergeableProperties.Invoke(null, [typeof(IndexerConfig)])!)
            .Select(property => property.Name)
            .ToList();
        var equalEnumerables = (bool)areEquivalentEnumerables.Invoke(null,
            [new[] { "a", "b" }, new[] { "a", "b" }])!;
        var differentEnumerables = (bool)areEquivalentEnumerables.Invoke(null,
            [new[] { "a", "b" }, new[] { "a", "c" }])!;
        var differentLengths = (bool)areEquivalentEnumerables.Invoke(null,
            [new[] { "a" }, new[] { "a", "b" }])!;

        Assert.Multiple(() =>
        {
            Assert.That(mergeableProperties, Does.Contain(nameof(IndexerConfig.Name)));
            Assert.That(mergeableProperties, Does.Not.Contain("Item"));
            Assert.That(equalEnumerables, Is.True);
            Assert.That(differentEnumerables, Is.False);
            Assert.That(differentLengths, Is.False);
        });
    }

    private interface ITestConfig
    {
    }

    private sealed class FirstConfig : ITestConfig
    {
        public string Name { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; }
        public NestedConfig Nested { get; set; } = new();
        public string[] Tags { get; set; } = [];
    }

    private sealed class SecondConfig : ITestConfig
    {
        public int Count { get; set; }
    }

    private sealed class NestedConfig
    {
        public string Marker { get; set; } = string.Empty;
    }

    private sealed class NoDefaultConfig(string label)
    {
        public string Label { get; set; } = label;
        public int Count { get; set; }
    }

    private sealed class IndexerConfig
    {
        public string Name { get; set; } = string.Empty;

        public string this[int index]
        {
            get => index.ToString();
            set
            {
            }
        }
    }
}
