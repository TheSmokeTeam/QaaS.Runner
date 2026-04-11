using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using QaaS.Framework.SDK;
using QaaS.Framework.Serialization;
using QaaS.Runner.Assertions.AssertionObjects;

namespace QaaS.Runner.Assertions.Tests;

[TestFixture]
public class BaseReporterTests
{
    private sealed class TestReporter : BaseReporter
    {
        public override void WriteTestResults(AssertionResult assertionResult)
        {
        }

        public static string ResolveAttachmentType(SerializationType? serializationType)
        {
            return GetAttachmentTypeBySerializationType(serializationType);
        }

        public static string ResolveMetadataSummary(IReadOnlyDictionary<string, string> metadataAttributes)
        {
            return BuildMetadataSummaryText(metadataAttributes);
        }
    }

    [TestCase(SerializationType.Binary, "application/octet-stream")]
    [TestCase(SerializationType.ProtobufMessage, "application/x-protobuff")]
    [TestCase(SerializationType.Json, "application/json")]
    [TestCase(SerializationType.Yaml, "application/yaml")]
    [TestCase(SerializationType.Xml, "application/xml")]
    [TestCase(SerializationType.XmlElement, "application/xml")]
    [TestCase(SerializationType.MessagePack, "application/x-msgpack")]
    [TestCase(null, "application/octet-stream")]
    public void GetAttachmentTypeBySerializationType_WithKnownTypes_ReturnsExpectedMimeType(
        SerializationType? serializationType, string expected)
    {
        var result = TestReporter.ResolveAttachmentType(serializationType);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void GetAttachmentTypeBySerializationType_WithUnsupportedType_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TestReporter.ResolveAttachmentType((SerializationType)999));
    }

    [Test]
    public void ExtractMetadataAttributes_ExcludesTeamAndSystemAndFlattensExtraLabels()
    {
        var metaData = new MetaDataConfig
        {
            Team = "Smoke",
            System = "QaaS"
        };
        var extraLabelsProperty = metaData.GetType().GetProperty("ExtraLabels");
        Assert.That(extraLabelsProperty, Is.Not.Null, "MetaDataConfig.ExtraLabels must exist for ReportPortal grouping.");
        var extraLabels = (IDictionary)Activator.CreateInstance(extraLabelsProperty!.PropertyType)!;
        extraLabels["Component"] = "Auth";
        extraLabels["Area"] = "Login";
        extraLabelsProperty.SetValue(metaData, extraLabels);

        var attributes = BaseReporter.ExtractMetadataAttributes(metaData);

        Assert.That(attributes.ContainsKey("Team"), Is.False);
        Assert.That(attributes.ContainsKey("System"), Is.False);
        Assert.That(attributes["Component"], Is.EqualTo("Auth"));
        Assert.That(attributes["Area"], Is.EqualTo("Login"));
    }

    [Test]
    public void BuildMetadataSummaryText_WithAttributes_FormatsDeterministically()
    {
        var summary = TestReporter.ResolveMetadataSummary(new Dictionary<string, string>
        {
            ["Owner"] = "Smoke",
            ["Component"] = "Gateway"
        });

        Assert.That(summary, Is.EqualTo("Component: Gateway" + Environment.NewLine + "Owner: Smoke"));
    }
}
