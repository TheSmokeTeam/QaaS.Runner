using System;
using NUnit.Framework;
using QaaS.Framework.Serialization;
using QaaS.Runner.Assertions.AssertionObjects;

namespace QaaS.Runner.Assertions.Tests;

[TestFixture]
public class BaseReporterTests
{
    private sealed class TestReporter : BaseReporter
    {
        public override ReporterKind Kind => ReporterKind.Allure;

        public override void WriteTestResults(AssertionResult assertionResult)
        {
        }

        public static string ResolveAttachmentType(SerializationType? serializationType)
        {
            return GetAttachmentTypeBySerializationType(serializationType);
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
}
