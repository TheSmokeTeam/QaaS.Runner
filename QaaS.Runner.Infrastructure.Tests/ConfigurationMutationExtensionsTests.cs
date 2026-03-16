using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace QaaS.Runner.Infrastructure.Tests;

[TestFixture]
public class ConfigurationMutationExtensionsTests
{
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
}
