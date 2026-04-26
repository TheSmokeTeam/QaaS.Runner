using System.IO.Abstractions;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.Serialization;
using AssertionResult = QaaS.Runner.Assertions.AssertionObjects.AssertionResult;
using AssertionSeverity = QaaS.Runner.Assertions.AssertionObjects.AssertionSeverity;


namespace QaaS.Runner.Assertions;

/// <inheritdoc />
public abstract class BaseReporter : IReporter
{
    protected const string TraceDisplayFalseMessage = "Assertion configured to not display assertion trace",
        QaaSTag = "QaaS",
        RawDataAttachmentType = "application/octet-stream",
        JsonAttachmentType = "application/json",
        XmlAttachmentType = "application/xml",
        YamlAttachmentType = "application/yaml",
        ProtobufAttachmentType = "application/x-protobuff",
        MessagePackAttachmentType = "application/x-msgpack";

    public Context Context = default!;

    public IFileSystem FileSystem = default!;

    public AssertionSeverity Severity { get; set; }
    public abstract ReporterKind Kind { get; }
    public string Name { get; set; } = string.Empty;
    public string AssertionName { get; set; } = string.Empty;
    public bool SaveSessionData { get; set; }
    public bool SaveLogs { get; set; } = true;
    public bool SaveAttachments { get; set; }
    public bool SaveTemplate { get; set; }
    public bool DisplayTrace { get; set; }
    public long EpochTestSuiteStartTime { get; set; }

    public abstract void WriteTestResults(AssertionResult assertionResult);

    protected static string GetAttachmentTypeBySerializationType(SerializationType? serializationType)
    {
        return serializationType switch
        {
            SerializationType.Binary => RawDataAttachmentType,
            SerializationType.ProtobufMessage => ProtobufAttachmentType,
            SerializationType.Json => JsonAttachmentType,
            SerializationType.Yaml => YamlAttachmentType,
            SerializationType.Xml => XmlAttachmentType,
            SerializationType.XmlElement => XmlAttachmentType,
            SerializationType.MessagePack => MessagePackAttachmentType,
            null => RawDataAttachmentType,
            _ => throw new InvalidOperationException($"Unsupported serialization type {serializationType} given")
        };
    }
}
