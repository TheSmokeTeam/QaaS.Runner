﻿using System.IO.Abstractions;
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

    /// <summary>
    /// Context information for the reporter
    /// </summary>
    public required Context Context;

    /// <summary>
    /// File system abstraction for the reporter
    /// </summary>
    public required IFileSystem FileSystem;

    /// <summary>
    /// Severity level of the assertion
    /// </summary>
    public AssertionSeverity Severity { get; set; }
    
    /// <summary>
    /// Name of the reporter
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// Whether to save session data
    /// </summary>
    public bool SaveSessionData { get; set; }
    
    /// <summary>
    /// Whether to save attachments
    /// </summary>
    public bool SaveAttachments { get; set; }
    
    /// <summary>
    /// Whether to save configuration template
    /// </summary>
    public bool SaveTemplate { get; set; }
    
    /// <summary>
    /// Whether to display assertion trace
    /// </summary>
    public bool DisplayTrace { get; set; }
    
    /// <summary>
    /// Epoch timestamp of when the test suite started
    /// </summary>
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