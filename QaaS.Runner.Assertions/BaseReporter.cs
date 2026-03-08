using System.Collections;
using System.Globalization;
using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QaaS.Framework.Configurations;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Assertions.AssertionObjects;
using QaaS.Runner.Infrastructure;
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
    public string Name { get; set; } = string.Empty;
    public string AssertionName { get; set; } = string.Empty;
    public bool SaveSessionData { get; set; }
    public bool SaveAttachments { get; set; }
    public bool SaveTemplate { get; set; }
    public bool DisplayTrace { get; set; }
    public long EpochTestSuiteStartTime { get; set; }

    public abstract void WriteTestResults(AssertionResult assertionResult);

    /// <summary>
    /// Converts custom assertion attachments into a reporter-neutral attachment model so all reporters can reuse the same
    /// source data.
    /// </summary>
    protected IReadOnlyList<ReportArtifact> BuildAssertionArtifacts(AssertionResult assertionResult)
    {
        if (!SaveAttachments)
            return [];

        var artifacts = new List<ReportArtifact>();
        foreach (var assertionAttachment in assertionResult.Assertion.AssertionHook?.AssertionAttachments ?? [])
        {
            var serializer = SerializerFactory.BuildSerializer(assertionAttachment.SerializationType);
            var serializedData = serializer?.Serialize(assertionAttachment.Data) ??
                                 (assertionAttachment.Data as byte[] ?? []);
            artifacts.Add(new ReportArtifact(
                assertionAttachment.Path,
                assertionAttachment.Path,
                serializedData,
                GetAttachmentTypeBySerializationType(assertionAttachment.SerializationType)));
        }

        return artifacts;
    }

    /// <summary>
    /// Builds a YAML artifact containing the effective execution template when template export is enabled.
    /// </summary>
    protected ReportArtifact? BuildTemplateArtifact()
    {
        if (!SaveTemplate)
            return null;

        return new ReportArtifact(
            "template.yaml",
            "template.yaml",
            System.Text.Encoding.UTF8.GetBytes(
                Context.RootConfiguration.BuildConfigurationAsYaml(
                    QaaS.Runner.Infrastructure.Constants.ConfigurationSectionNames)),
            YamlAttachmentType);
    }

    /// <summary>
    /// Builds a JSON artifact containing one session payload when session export is enabled.
    /// </summary>
    protected ReportArtifact? BuildSessionArtifact(SessionData sessionData)
    {
        if (!SaveSessionData)
            return null;

        return new ReportArtifact(
            $"{sessionData.Name}.json",
            $"{sessionData.Name}.json",
            SessionDataSerialization.SerializeSessionData(sessionData,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                }),
            JsonAttachmentType);
    }

    /// <summary>
    /// Extracts reporter attributes from execution metadata. Team and System are excluded because they are emitted as
    /// first-class tags separately, while ExtraLabels-style key/value data is flattened into individual attributes.
    /// </summary>
    protected IReadOnlyDictionary<string, string> BuildMetadataAttributes()
    {
        if (Context is not InternalContext internalContext)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return ExtractMetadataAttributes(internalContext.GetMetaDataOrDefault());
    }

    /// <summary>
    /// Builds a reporter-neutral JSON artifact describing the assertion, execution, session, and metadata context that
    /// produced the current result. Downstream reporters can attach or log it without rebuilding the same structure.
    /// </summary>
    protected ReportArtifact BuildAssertionContextArtifact(AssertionResult assertionResult, string? team = null,
        string? system = null)
    {
        var metadataAttributes = BuildMetadataAttributes();
        var dataSourceSummaries = (assertionResult.Assertion.DataSourceList ?? [])
            .Select(dataSource => new
            {
                dataSource.Name,
                dataSource.Lazy
            })
            .ToList();
        var sessionSummaries = assertionResult.Assertion.SessionDataList
            .Select(sessionData => new
            {
                sessionData.Name,
                sessionData.UtcStartTime,
                sessionData.UtcEndTime,
                DurationMs = (long)Math.Max(0, (sessionData.UtcEndTime - sessionData.UtcStartTime).TotalMilliseconds),
                Inputs = (sessionData.Inputs ?? []).Select(input => input.Name).ToArray(),
                Outputs = (sessionData.Outputs ?? []).Select(output => output.Name).ToArray(),
                Failures = sessionData.SessionFailures.Select(actionFailure => new
                {
                    actionFailure.Action,
                    actionFailure.ActionType,
                    actionFailure.Name,
                    actionFailure.Reason.Description,
                    actionFailure.Reason.Message
                }).ToArray()
            })
            .ToList();

        var payload = new
        {
            Tool = QaaSTag,
            Assertion = new
            {
                assertionResult.Assertion.Name,
                assertionResult.Assertion.AssertionName,
                Status = assertionResult.AssertionStatus.ToString(),
                Severity = Severity.ToString(),
                Flaky = assertionResult.Flaky.IsFlaky,
                DurationMs = assertionResult.TestDurationMs
            },
            Execution = new
            {
                Context.ExecutionId,
                Context.CaseName,
                Team = team,
                System = system,
                Identity = !string.IsNullOrWhiteSpace(team) && !string.IsNullOrWhiteSpace(system)
                    ? BuildStableReportPortalIdentity(assertionResult, team, system)
                    : null
            },
            Links = assertionResult.Links?.Select(link => new { Name = link.Key, Url = link.Value }).ToArray() ?? [],
            Metadata = metadataAttributes,
            DataSources = dataSourceSummaries,
            Sessions = sessionSummaries
        };

        return new ReportArtifact(
            "assertion-context.json",
            Path.Combine("reportportal", "context", "assertion-context.json"),
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            })),
            JsonAttachmentType);
    }

    /// <summary>
    /// Builds a stable ReportPortal identity that keeps history attached to the same team/system/assertion/metadata
    /// combination across runs.
    /// </summary>
    protected string BuildStableReportPortalIdentity(AssertionResult assertionResult, string team, string system)
    {
        var identityParts = new List<string>
        {
            QaaSTag,
            NormalizeIdentitySegment(team),
            NormalizeIdentitySegment(system),
            NormalizeIdentitySegment(Context.CaseName),
            NormalizeIdentitySegment(assertionResult.Assertion.AssertionName),
            NormalizeIdentitySegment(assertionResult.Assertion.Name),
            NormalizeIdentitySegment(string.Join(",",
                assertionResult.Assertion.SessionDataList
                    .Select(sessionData => sessionData.Name)
                    .Where(sessionName => !string.IsNullOrWhiteSpace(sessionName))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(sessionName => sessionName, StringComparer.OrdinalIgnoreCase)))
        };

        foreach (var attribute in BuildMetadataAttributes().OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase))
        {
            identityParts.Add(NormalizeIdentitySegment(attribute.Key));
            identityParts.Add(NormalizeIdentitySegment(attribute.Value));
        }

        return string.Join("::", identityParts);
    }

    /// <summary>
    /// Builds human-readable assertion details for reporting.
    /// </summary>
    protected AssertionTextDetails BuildAssertionTextDetails(AssertionResult assertionResult)
    {
        if (assertionResult.AssertionStatus == AssertionStatus.Broken)
        {
            return new AssertionTextDetails(
                assertionResult.BrokenAssertionException?.Message ?? "Broken assertion",
                DisplayTrace
                    ? assertionResult.BrokenAssertionException?.ToString() ?? string.Empty
                    : TraceDisplayFalseMessage);
        }

        return new AssertionTextDetails(
            assertionResult.Assertion.AssertionHook?.AssertionMessage ?? string.Empty,
            DisplayTrace
                ? assertionResult.Assertion.AssertionHook?.AssertionTrace ?? string.Empty
                : TraceDisplayFalseMessage);
    }

    /// <summary>
    /// Builds the common textual session summary used by downstream reporters.
    /// </summary>
    protected static string BuildSessionSummaryText(SessionData sessionData)
    {
        return
            $"Session: {sessionData.Name}{Environment.NewLine}" +
            $"Inputs: [{string.Join(", ", (sessionData.Inputs ?? []).Select(input => input.Name))}]{Environment.NewLine}" +
            $"Outputs: [{string.Join(", ", (sessionData.Outputs ?? []).Select(output => output.Name))}]{Environment.NewLine}" +
            $"Start: {sessionData.UtcStartTime:O}{Environment.NewLine}" +
            $"End: {sessionData.UtcEndTime:O}{Environment.NewLine}" +
            $"Failures: {sessionData.SessionFailures.Count}";
    }

    /// <summary>
    /// Builds the per-session action failure text used by downstream reporters.
    /// </summary>
    protected static string BuildActionFailureText(SessionData sessionData, ActionFailure actionFailure)
    {
        return
            $"Session `{sessionData.Name}` action failure.{Environment.NewLine}" +
            $"Action: {actionFailure.Action}{Environment.NewLine}" +
            $"Action type: {actionFailure.ActionType}{Environment.NewLine}" +
            $"Name: {actionFailure.Name}{Environment.NewLine}" +
            $"Reason: {actionFailure.Reason.Description}{Environment.NewLine}" +
            $"Message: {actionFailure.Reason.Message}";
    }

    /// <summary>
    /// Builds flakiness text shared across reporters.
    /// </summary>
    protected static string BuildFlakinessText(
        IEnumerable<KeyValuePair<string, List<ActionFailure>>> flakinessReasons)
    {
        var builder = new System.Text.StringBuilder("Flakiness reasons:");
        foreach (var sessionFailurePair in flakinessReasons)
        {
            foreach (var sessionFailure in sessionFailurePair.Value)
            {
                builder.AppendLine()
                    .Append("- Session ")
                    .Append(sessionFailurePair.Key)
                    .Append(": ")
                    .Append(sessionFailure.Name)
                    .Append(" (")
                    .Append(sessionFailure.ActionType)
                    .Append(") -> ")
                    .Append(sessionFailure.Reason.Message);
            }
        }

        return builder.ToString();
    }

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

    /// <summary>
    /// Creates a compact multi-line string of metadata key/value pairs for reporter descriptions and logs.
    /// </summary>
    protected static string BuildMetadataSummaryText(IReadOnlyDictionary<string, string> metadataAttributes)
    {
        if (metadataAttributes.Count == 0)
            return "No metadata attributes were provided.";

        return string.Join(Environment.NewLine,
            metadataAttributes.OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
                .Select(attribute => $"{attribute.Key}: {attribute.Value}"));
    }

    internal static IReadOnlyDictionary<string, string> ExtractMetadataAttributes(MetaDataConfig metaData)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var metaDataType = metaData.GetType();
        foreach (var property in metaDataType.GetProperties()
                     .Where(property => property.CanRead && property.GetIndexParameters().Length == 0))
        {
            if (string.Equals(property.Name, nameof(MetaDataConfig.Team), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.Name, nameof(MetaDataConfig.System), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var propertyValue = property.GetValue(metaData);
            if (propertyValue is null)
                continue;

            AddMetadataProperty(attributes, property.Name, propertyValue,
                string.Equals(property.Name, "ExtraLabels", StringComparison.OrdinalIgnoreCase));
        }

        return attributes;
    }

    private static void AddMetadataProperty(IDictionary<string, string> attributes, string propertyName, object propertyValue,
        bool useNestedKeyOnly)
    {
        if (TryConvertToString(propertyValue, out var scalarValue))
        {
            attributes[propertyName] = scalarValue;
            return;
        }

        if (propertyValue is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                var entryKey = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(entryKey) || entry.Value is null)
                    continue;

                if (!TryConvertToString(entry.Value, out var entryValue))
                    entryValue = JsonSerializer.Serialize(entry.Value);

                attributes[useNestedKeyOnly ? entryKey : $"{propertyName}:{entryKey}"] = entryValue;
            }

            return;
        }

        if (propertyValue is IEnumerable enumerable and not string)
        {
            var keyValuePairs = new List<KeyValuePair<string, string>>();
            var scalarValues = new List<string>();
            foreach (var item in enumerable)
            {
                if (item is null)
                    continue;

                var itemType = item.GetType();
                if (itemType.IsGenericType &&
                    string.Equals(itemType.GetGenericTypeDefinition().FullName, typeof(KeyValuePair<,>).FullName,
                        StringComparison.Ordinal))
                {
                    var keyProperty = itemType.GetProperty("Key");
                    var valueProperty = itemType.GetProperty("Value");
                    var nestedKey = Convert.ToString(keyProperty?.GetValue(item), CultureInfo.InvariantCulture);
                    var nestedValueObject = valueProperty?.GetValue(item);
                    if (string.IsNullOrWhiteSpace(nestedKey) || nestedValueObject is null)
                        continue;

                    if (!TryConvertToString(nestedValueObject, out var nestedValue))
                        nestedValue = JsonSerializer.Serialize(nestedValueObject);

                    keyValuePairs.Add(new KeyValuePair<string, string>(
                        useNestedKeyOnly ? nestedKey : $"{propertyName}:{nestedKey}",
                        nestedValue));
                    continue;
                }

                if (TryConvertToString(item, out var itemValue))
                    scalarValues.Add(itemValue);
            }

            if (keyValuePairs.Count > 0)
            {
                foreach (var keyValuePair in keyValuePairs)
                    attributes[keyValuePair.Key] = keyValuePair.Value;
                return;
            }

            if (scalarValues.Count > 0)
            {
                attributes[propertyName] = string.Join(", ", scalarValues);
                return;
            }
        }

        attributes[propertyName] = JsonSerializer.Serialize(propertyValue);
    }

    private static bool TryConvertToString(object value, out string convertedValue)
    {
        convertedValue = string.Empty;
        switch (value)
        {
            case string stringValue when !string.IsNullOrWhiteSpace(stringValue):
                convertedValue = stringValue.Trim();
                return true;
            case bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                convertedValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                return true;
            case Guid guid when guid != Guid.Empty:
                convertedValue = guid.ToString("D");
                return true;
            case DateTime dateTime:
                convertedValue = dateTime.ToString("O", CultureInfo.InvariantCulture);
                return true;
            case DateTimeOffset dateTimeOffset:
                convertedValue = dateTimeOffset.ToString("O", CultureInfo.InvariantCulture);
                return true;
            case Enum enumValue:
                convertedValue = enumValue.ToString();
                return true;
            default:
                return false;
        }
    }

    private static string NormalizeIdentitySegment(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "<none>"
            : value.Trim().Replace("::", ":", StringComparison.Ordinal).ToLowerInvariant();
    }
}

/// <summary>
/// Reporter-neutral attachment model consumed by downstream reporters.
/// </summary>
public sealed record ReportArtifact(string Name, string RelativePath, byte[] Content, string ContentType);

/// <summary>
/// Reporter-neutral assertion message/trace model.
/// </summary>
public sealed record AssertionTextDetails(string Message, string Trace);
