using System.ComponentModel.DataAnnotations;
using System.Reflection;
using QaaS.Framework.Policies;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.Sessions.Actions.Collectors;
using QaaS.Runner.Sessions.Actions.Consumers.Builders;
using QaaS.Runner.Sessions.Actions.MockerCommands;
using QaaS.Runner.Sessions.Actions.Publishers.Builders;
using QaaS.Runner.Sessions.Actions.Transactions.Builders;
using QaaS.Runner.Sessions.ConfigurationObjects;
using QaaS.Runner.Storage;
using ServerConfig = QaaS.Mocker.Servers.ConfigurationObjects.ServerConfig;

internal static class SweepNodes
{
    private static readonly Dictionary<Type, string[]> UnionProperties = new()
    {
        [typeof(StorageBuilder)] = ["FileSystem", "S3"],
        [typeof(LinkBuilder)] = ["Kibana", "Prometheus", "Grafana"],
        [typeof(PublisherBuilder)] =
        [
            "RabbitMq", "KafkaTopic", "Redis", "PostgreSqlTable", "OracleSqlTable", "S3Bucket", "Socket",
            "ElasticIndex", "MsSqlTable", "MongoDbCollection", "Sftp"
        ],
        [typeof(ConsumerBuilder)] =
        [
            "RabbitMq", "KafkaTopic", "MsSqlTable", "OracleSqlTable", "TrinoSqlTable", "PostgreSqlTable",
            "S3Bucket", "ElasticIndices", "Socket", "IbmMqQueue"
        ],
        [typeof(TransactionBuilder)] = ["Http", "Grpc"],
        [typeof(CollectorBuilder)] = ["Prometheus"],
        [typeof(ServerConfig)] = ["Http", "Grpc", "Socket"],
        [typeof(MockerCommandConfig)] = ["ChangeActionStub", "TriggerAction", "Consume"],
        [typeof(PolicyBuilder)] = ["Count", "Timeout", "LoadBalance", "IncreasingLoadBalance", "AdvancedLoadBalance"]
    };

    private static readonly Dictionary<Type, string[]> AlwaysIncludeProperties = new()
    {
        [typeof(MockerCommandConfig)] = ["Consume"],
        [typeof(PolicyBuilder)] = ["Count"]
    };

    /// <summary>
    /// Enumerates writable configuration fields for a context so the filled variant can assign explicit sample values.
    /// </summary>
    public static IEnumerable<FieldDescriptor> EnumerateFields(ContextSpec context)
    {
        return EnumerateFieldsRecursive(context.TargetType, context, [], string.Empty, 0);
    }

    /// <summary>
    /// Creates the smallest node that still satisfies unconditional required-field rules for the selected variant.
    /// </summary>
    public static object? BuildMinimalNode(Type type, ContextSpec context, SupportPaths support, bool forceInclude)
    {
        var unwrappedType = Nullable.GetUnderlyingType(type) ?? type;

        if (unwrappedType == typeof(string))
        {
            return "value";
        }

        if (unwrappedType == typeof(bool))
        {
            return false;
        }

        if (unwrappedType.IsEnum)
        {
            return Enum.GetValues(unwrappedType).GetValue(0);
        }

        if (unwrappedType == typeof(int) || unwrappedType == typeof(short) || unwrappedType == typeof(long))
        {
            return 1;
        }

        if (unwrappedType == typeof(uint) || unwrappedType == typeof(ulong))
        {
            return 1;
        }

        if (unwrappedType == typeof(double) || unwrappedType == typeof(float) || unwrappedType == typeof(decimal))
        {
            return 1;
        }

        if (typeof(Microsoft.Extensions.Configuration.IConfiguration).IsAssignableFrom(unwrappedType))
        {
            return new Dictionary<string, object?>();
        }

        if (typeof(System.Collections.IDictionary).IsAssignableFrom(unwrappedType))
        {
            return new Dictionary<string, object?>();
        }

        if (TryGetCollectionElementType(unwrappedType, out var elementType))
        {
            return new List<object?> { BuildMinimalNode(elementType!, context, support, true) };
        }

        var properties = GetRelevantProperties(unwrappedType).ToList();
        var include = new HashSet<string>(StringComparer.Ordinal);

        if (UnionProperties.TryGetValue(unwrappedType, out var unionProps))
        {
            var selected = ResolveSelectedVariant(context, unwrappedType, unionProps);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                include.Add(selected!);
            }
        }

        foreach (var property in properties)
        {
            if (property.DeclaringType == context.TargetType && context.AlwaysIncludeRootProperties.Contains(property.Name))
            {
                include.Add(property.Name);
            }

            if (AlwaysIncludeProperties.TryGetValue(unwrappedType, out var always) && always.Contains(property.Name))
            {
                include.Add(property.Name);
            }

            if (property.GetCustomAttribute<RequiredAttribute>() != null)
            {
                include.Add(property.Name);
            }

            var minLength = property.GetCustomAttribute<MinLengthAttribute>();
            if (minLength is { Length: > 0 })
            {
                include.Add(property.Name);
            }
        }

        if (forceInclude && include.Count == 0)
        {
            var firstProperty = properties.FirstOrDefault(property =>
                !IsIgnoredUnionSibling(unwrappedType, property.Name, context));
            if (firstProperty != null)
            {
                include.Add(firstProperty.Name);
            }
        }

        var node = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (!include.Contains(property.Name) || IsIgnoredUnionSibling(unwrappedType, property.Name, context))
            {
                continue;
            }

            node[property.Name] = BuildMinimalPropertyValue(
                unwrappedType,
                property,
                context,
                support,
                property.DeclaringType == context.TargetType && context.AlwaysIncludeRootProperties.Contains(property.Name));
        }

        return node;
    }

    /// <summary>
    /// Produces an explicit sample value for a concrete field when the filled variant wants a non-default value.
    /// </summary>
    public static object? CreateExplicitNode(Type ownerType, PropertyInfo property, Type fieldType, SupportPaths support)
    {
        var unwrappedType = Nullable.GetUnderlyingType(fieldType) ?? fieldType;

        if (unwrappedType == typeof(string))
        {
            return CreateStringSample(ownerType, property.Name, support);
        }

        if (unwrappedType == typeof(bool))
        {
            return true;
        }

        if (unwrappedType.IsEnum)
        {
            if (string.Equals(property.Name, "ProtocolType", StringComparison.Ordinal))
            {
                return Enum.Parse(unwrappedType, "Tcp");
            }

            if (string.Equals(property.Name, "SocketType", StringComparison.Ordinal))
            {
                return Enum.Parse(unwrappedType, "Stream");
            }

            if (string.Equals(property.Name, "AddressFamily", StringComparison.Ordinal))
            {
                return Enum.Parse(unwrappedType, "InterNetwork");
            }

            var values = Enum.GetValues(unwrappedType);
            return values.GetValue(values.Length > 1 ? 1 : 0);
        }

        if (unwrappedType == typeof(int) || unwrappedType == typeof(short) || unwrappedType == typeof(long))
        {
            if (string.Equals(property.Name, "MessageMaxBytes", StringComparison.Ordinal))
            {
                return 1_000_000;
            }

            if (property.Name.Contains("Port", StringComparison.OrdinalIgnoreCase))
            {
                return 8080;
            }

            return property.Name.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ? 1000 : 2;
        }

        if (unwrappedType == typeof(uint) || unwrappedType == typeof(ulong))
        {
            return 2;
        }

        if (unwrappedType == typeof(double) || unwrappedType == typeof(float) || unwrappedType == typeof(decimal))
        {
            return 2;
        }

        if (typeof(System.Collections.IDictionary).IsAssignableFrom(unwrappedType))
        {
            return new Dictionary<string, object?> { ["key"] = "value" };
        }

        if (typeof(Microsoft.Extensions.Configuration.IConfiguration).IsAssignableFrom(unwrappedType))
        {
            return new Dictionary<string, object?> { ["Sample"] = "value" };
        }

        if (TryGetCollectionElementType(unwrappedType, out var elementType))
        {
            if ((Nullable.GetUnderlyingType(elementType!) ?? elementType!) == typeof(string))
            {
                return new List<object?> { CreateStringSample(ownerType, property.Name, support) };
            }

            return new List<object?> { BuildMinimalNode(elementType!, new ContextSpec(
                "adhoc", "adhoc", elementType!, ValidationKind.RunnerTemplate, "adhoc", "yaml",
                (_, _, _) => new Dictionary<string, object?>()), support, true) };
        }

        return BuildMinimalNode(unwrappedType, new ContextSpec(
            "adhoc", "adhoc", unwrappedType, ValidationKind.RunnerTemplate, "adhoc", "yaml",
            (_, _, _) => new Dictionary<string, object?>()), support, true);
    }

    public static void ApplyOverrides(object? root, IReadOnlyDictionary<string, object?> overrides)
    {
        foreach (var pair in overrides)
        {
            var tokens = ParsePath(pair.Key);
            var current = root;
            SetNodeValue(ref current, tokens, pair.Value);
        }
    }

    /// <summary>
    /// Removes conflicting or intentionally blank paths from a generated node before serialization.
    /// </summary>
    public static void RemovePaths(ref object? root, IReadOnlyCollection<string> paths)
    {
        foreach (var path in paths)
        {
            RemoveNodeValue(ref root, ParsePath(path));
        }
    }

    public static IReadOnlyList<PathToken> ParsePath(string path)
    {
        var tokens = new List<PathToken>();
        var buffer = string.Empty;

        for (var index = 0; index < path.Length; index++)
        {
            var current = path[index];
            if (current == '.')
            {
                if (!string.IsNullOrWhiteSpace(buffer))
                {
                    tokens.Add(PathToken.Property(buffer));
                    buffer = string.Empty;
                }

                continue;
            }

            if (current == '[')
            {
                if (!string.IsNullOrWhiteSpace(buffer))
                {
                    tokens.Add(PathToken.Property(buffer));
                    buffer = string.Empty;
                }

                var closing = path.IndexOf(']', index);
                var value = int.Parse(path.Substring(index + 1, closing - index - 1));
                tokens.Add(PathToken.Item(value));
                index = closing;
                continue;
            }

            buffer += current;
        }

        if (!string.IsNullOrWhiteSpace(buffer))
        {
            tokens.Add(PathToken.Property(buffer));
        }

        return tokens;
    }

    public static void SetNodeValue(ref object? root, IReadOnlyList<PathToken> tokens, object? value)
    {
        if (tokens.Count == 0)
        {
            root = DeepCloneNode(value);
            return;
        }

        root ??= tokens[0].Kind == PathTokenKind.Property
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new List<object?>();

        object? current = root;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            var isLast = index == tokens.Count - 1;

            if (token.Kind == PathTokenKind.Property)
            {
                var dictionary = current as Dictionary<string, object?> ??
                                 throw new InvalidOperationException("Expected dictionary node while setting a property path.");
                if (isLast)
                {
                    dictionary[token.Name!] = DeepCloneNode(value);
                    return;
                }

                if (!dictionary.TryGetValue(token.Name!, out var next) || next == null)
                {
                    next = tokens[index + 1].Kind == PathTokenKind.Property
                        ? new Dictionary<string, object?>(StringComparer.Ordinal)
                        : new List<object?>();
                    dictionary[token.Name!] = next;
                }

                current = next;
                continue;
            }

            var list = current as List<object?> ??
                       throw new InvalidOperationException("Expected list node while setting an indexed path.");
            while (list.Count <= token.Index)
            {
                list.Add(null);
            }

            if (isLast)
            {
                list[token.Index] = DeepCloneNode(value);
                return;
            }

            if (list[token.Index] == null)
            {
                list[token.Index] = tokens[index + 1].Kind == PathTokenKind.Property
                    ? new Dictionary<string, object?>(StringComparer.Ordinal)
                    : new List<object?>();
            }

            current = list[token.Index];
        }
    }

    public static void RemoveNodeValue(ref object? root, IReadOnlyList<PathToken> tokens)
    {
        if (root == null || tokens.Count == 0)
        {
            return;
        }

        object? current = root;
        for (var index = 0; index < tokens.Count - 1; index++)
        {
            var token = tokens[index];
            if (token.Kind == PathTokenKind.Property)
            {
                if (current is not Dictionary<string, object?> dictionary ||
                    !dictionary.TryGetValue(token.Name!, out current) ||
                    current == null)
                {
                    return;
                }
            }
            else
            {
                if (current is not List<object?> list || list.Count <= token.Index || list[token.Index] == null)
                {
                    return;
                }

                current = list[token.Index];
            }
        }

        var last = tokens[^1];
        if (last.Kind == PathTokenKind.Property)
        {
            if (current is Dictionary<string, object?> dictionary)
            {
                dictionary.Remove(last.Name!);
            }
        }
        else if (current is List<object?> list && list.Count > last.Index)
        {
            list[last.Index] = null;
        }
    }

    public static object? DeepCloneNode(object? node)
    {
        return node switch
        {
            null => null,
            Dictionary<string, object?> dictionary => dictionary.ToDictionary(
                pair => pair.Key,
                pair => DeepCloneNode(pair.Value),
                StringComparer.Ordinal),
            List<object?> list => list.Select(DeepCloneNode).ToList(),
            _ => node
        };
    }

    private static IEnumerable<FieldDescriptor> EnumerateFieldsRecursive(
        Type type,
        ContextSpec context,
        IReadOnlyList<PathToken> currentTokens,
        string prefix,
        int depth)
    {
        if (depth > 8)
        {
            yield break;
        }

        var unwrappedType = Nullable.GetUnderlyingType(type) ?? type;
        if (TryGetCollectionElementType(unwrappedType, out var elementType))
        {
            var listPrefix = string.IsNullOrEmpty(prefix) ? "[0]" : $"{prefix}[0]";
            var listTokens = currentTokens.Concat([PathToken.Item(0)]).ToArray();
            foreach (var child in EnumerateFieldsRecursive(elementType!, context, listTokens, listPrefix, depth + 1))
            {
                yield return child;
            }

            yield break;
        }

        if (IsTerminalType(unwrappedType))
        {
            yield break;
        }

        foreach (var property in GetRelevantProperties(unwrappedType))
        {
            if (IsIgnoredUnionSibling(unwrappedType, property.Name, context))
            {
                continue;
            }

            var propertyPrefix = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
            var tokens = currentTokens.Concat([PathToken.Property(property.Name)]).ToArray();
            yield return new FieldDescriptor(propertyPrefix, tokens, property.PropertyType, unwrappedType, property);

            if (unwrappedType == context.TargetType && context.TerminalProperties.Contains(property.Name))
            {
                continue;
            }

            foreach (var child in EnumerateFieldsRecursive(property.PropertyType, context, tokens, propertyPrefix, depth + 1))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<PropertyInfo> GetRelevantProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(property =>
                property.CanRead &&
                property.GetMethod != null &&
                property.GetMethod.GetParameters().Length == 0 &&
                property.SetMethod != null &&
                !property.GetMethod.IsStatic)
            .OrderBy(property => property.MetadataToken);
    }

    private static bool TryGetCollectionElementType(Type type, out Type? elementType)
    {
        if (type == typeof(string))
        {
            elementType = null;
            return false;
        }

        if (type.IsArray)
        {
            elementType = type.GetElementType();
            return elementType != null;
        }

        var enumerableInterface = type.GetInterfaces()
            .FirstOrDefault(interfaceType =>
                interfaceType.IsGenericType &&
                interfaceType.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        if (enumerableInterface != null)
        {
            elementType = enumerableInterface.GetGenericArguments()[0];
            return true;
        }

        elementType = null;
        return false;
    }

    private static bool IsTerminalType(Type type)
    {
        return type.IsPrimitive ||
               type.IsEnum ||
               type == typeof(string) ||
               type == typeof(decimal) ||
               typeof(Microsoft.Extensions.Configuration.IConfiguration).IsAssignableFrom(type) ||
               typeof(System.Collections.IDictionary).IsAssignableFrom(type);
    }

    private static bool IsIgnoredUnionSibling(Type type, string propertyName, ContextSpec context)
    {
        if (!UnionProperties.TryGetValue(type, out var variants))
        {
            return false;
        }

        var selected = ResolveSelectedVariant(context, type, variants);
        return !string.IsNullOrWhiteSpace(selected) &&
               variants.Contains(propertyName, StringComparer.Ordinal) &&
               !string.Equals(selected, propertyName, StringComparison.Ordinal);
    }

    private static string? ResolveSelectedVariant(ContextSpec context, Type type, IEnumerable<string> variants)
    {
        if (context.SelectedVariants.TryGetValue(type, out var selected))
        {
            return selected;
        }

        return variants.FirstOrDefault();
    }

    private static object? BuildMinimalPropertyValue(
        Type ownerType,
        PropertyInfo property,
        ContextSpec context,
        SupportPaths support,
        bool forceInclude)
    {
        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (propertyType == typeof(string))
        {
            return CreateStringSample(ownerType, property.Name, support);
        }

        if (propertyType == typeof(bool))
        {
            return false;
        }

        if (propertyType.IsEnum)
        {
            return Enum.GetValues(propertyType).GetValue(0);
        }

        if (propertyType == typeof(int) || propertyType == typeof(short) || propertyType == typeof(long))
        {
            if (property.Name.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
            {
                return 1000;
            }

            if (property.Name.Contains("Port", StringComparison.OrdinalIgnoreCase))
            {
                return 8080;
            }

            return 1;
        }

        if (propertyType == typeof(uint) || propertyType == typeof(ulong))
        {
            return 1;
        }

        if (propertyType == typeof(double) || propertyType == typeof(float) || propertyType == typeof(decimal))
        {
            return 1;
        }

        if (typeof(Microsoft.Extensions.Configuration.IConfiguration).IsAssignableFrom(propertyType))
        {
            return new Dictionary<string, object?>();
        }

        if (typeof(System.Collections.IDictionary).IsAssignableFrom(propertyType))
        {
            return new Dictionary<string, object?>();
        }

        if (TryGetCollectionElementType(propertyType, out var elementType))
        {
            return new List<object?> { BuildMinimalCollectionElement(ownerType, property.Name, elementType!, context, support) };
        }

        return BuildMinimalNode(propertyType, context, support, forceInclude);
    }

    private static object? BuildMinimalCollectionElement(
        Type ownerType,
        string propertyName,
        Type elementType,
        ContextSpec context,
        SupportPaths support)
    {
        var unwrappedElementType = Nullable.GetUnderlyingType(elementType) ?? elementType;
        if (unwrappedElementType == typeof(string))
        {
            return CreateStringSample(ownerType, propertyName, support);
        }

        return BuildMinimalNode(unwrappedElementType, context, support, true);
    }

    private static string CreateStringSample(Type ownerType, string propertyName, SupportPaths support)
    {
        if (string.Equals(propertyName, "BaseAddress", StringComparison.Ordinal))
        {
            return "http://localhost";
        }

        if (ownerType.FullName == "QaaS.Mocker.Servers.ConfigurationObjects.HttpServerConfigs.HttpEndpointConfig" &&
            string.Equals(propertyName, "Path", StringComparison.Ordinal))
        {
            return "/api/{id}";
        }

        if (propertyName.Contains("CertificatePath", StringComparison.OrdinalIgnoreCase))
        {
            return support.CertificatePath;
        }

        if (propertyName.Contains("SchemaPath", StringComparison.OrdinalIgnoreCase))
        {
            return support.SchemaPath;
        }

        if (propertyName.EndsWith("Path", StringComparison.OrdinalIgnoreCase))
        {
            return support.SampleFilePath;
        }

        if (propertyName.Contains("Folder", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Contains("Directory", StringComparison.OrdinalIgnoreCase))
        {
            return support.FoldersDirectory;
        }

        if (propertyName.Contains("Url", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Contains("Uri", StringComparison.OrdinalIgnoreCase))
        {
            return "http://localhost";
        }

        if (propertyName.Contains("Host", StringComparison.OrdinalIgnoreCase))
        {
            return "localhost";
        }

        if (propertyName.Contains("Ip", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Contains("Address", StringComparison.OrdinalIgnoreCase))
        {
            return "127.0.0.1";
        }

        if (propertyName.Contains("Password", StringComparison.OrdinalIgnoreCase))
        {
            return "password";
        }

        if (propertyName.Contains("User", StringComparison.OrdinalIgnoreCase))
        {
            return "user";
        }

        if (string.Equals(propertyName, "AssemblyName", StringComparison.Ordinal))
        {
            return Assembly.GetExecutingAssembly().GetName().Name ?? "SweepHarness";
        }

        if (string.Equals(propertyName, "TypeFullName", StringComparison.Ordinal))
        {
            return typeof(SweepProgram).FullName ?? "SweepProgram";
        }

        if (propertyName.Contains("Pattern", StringComparison.OrdinalIgnoreCase))
        {
            return "support-.*";
        }

        return propertyName.ToLowerInvariant() switch
        {
            "team" => "SweepTeam",
            "system" => "SweepSystem",
            "id" => "sample-id",
            "servername" => "controller-server",
            "actionname" => "action-1",
            "stubname" => support.SupportStubName,
            "transactionstubname" => support.SupportStubName,
            "datasourcename" => support.SupportDataSourceName,
            "datasourcenames" => support.SupportDataSourceName,
            "sessionname" => support.SupportSessionName,
            "sessionnames" => support.SupportSessionName,
            "datasourcepatterns" => "support-.*",
            "sessionnamepatterns" => "support-session.*",
            "hierarchicalclaims" => "claim:\n  nested: value",
            _ => "value"
        };
    }
}
