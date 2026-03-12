using System.Collections;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using QaaS.Framework.Configurations.ConfigurationBindingUtils;
using YamlDotNet.Serialization;

namespace QaaS.Runner.Infrastructure;

/// <summary>
/// Renders a stable YAML template from the resolved runner builders while preserving explicit configuration values.
/// </summary>
public static class ConfigurationTemplateRenderer
{
    private static readonly BindingFlags ConfigPropertyBindingFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// Serializes the resolved configuration in a stable section order while keeping explicitly configured defaults
    /// and augmenting assertion statuses that are resolved at runtime.
    /// </summary>
    public static string Render(
        IConfiguration rootConfiguration,
        IEnumerable<KeyValuePair<string, object?>>? fallbackSections = null,
        IEnumerable<string>? sectionOrder = null,
        ISet<string>? includedSessionNames = null,
        IDictionary<string, IReadOnlyList<string>>? assertionStatusesToReport = null)
    {
        var orderedSections = (sectionOrder ?? Constants.ConfigurationSectionNames).ToList();
        var configuredPaths = GetConfiguredPaths(rootConfiguration);
        var rootSections = rootConfiguration.GetDictionaryFromConfiguration()
            .ToDictionary(section => section.Key, section => NormalizeValue(section.Value), StringComparer.Ordinal);
        var fallbackSectionMap = (fallbackSections ?? [])
            .ToDictionary(
                section => section.Key,
                section => SerializeValue(section.Value, section.Key, null, configuredPaths),
                StringComparer.Ordinal);
        var assertionNames = assertionStatusesToReport?.Keys.ToHashSet(StringComparer.Ordinal);

        var serializedSections = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var sectionName in orderedSections)
        {
            if (!TryGetSectionValue(sectionName, fallbackSectionMap, rootSections, out var serializedValue))
            {
                continue;
            }

            serializedValue = sectionName switch
            {
                "Sessions" => FilterNamedSection(serializedValue, includedSessionNames),
                "Assertions" => AugmentAssertionStatuses(
                    FilterNamedSection(serializedValue, assertionNames),
                    assertionStatusesToReport),
                _ => serializedValue
            };

            if (serializedValue == null || IsEmptyContainer(serializedValue))
            {
                continue;
            }

            serializedSections[sectionName] = serializedValue;
        }

        return new SerializerBuilder()
            .WithIndentedSequences()
            .Build()
            .Serialize(serializedSections);
    }

    private static bool TryGetSectionValue(
        string sectionName,
        IReadOnlyDictionary<string, object?> fallbackSections,
        IReadOnlyDictionary<string, object?> rootSections,
        out object? sectionValue)
    {
        foreach (var key in ResolveSectionAliases(sectionName))
        {
            if (fallbackSections.TryGetValue(key, out sectionValue))
            {
                return true;
            }

            if (rootSections.TryGetValue(key, out sectionValue))
            {
                return true;
            }
        }

        sectionValue = null;
        return false;
    }

    private static IEnumerable<string> ResolveSectionAliases(string sectionName)
    {
        yield return sectionName;

        if (sectionName == "Storages")
        {
            yield return "Storage";
        }
    }

    private static HashSet<string> GetConfiguredPaths(IConfiguration configuration)
    {
        var configuredPaths = new HashSet<string>(StringComparer.Ordinal);
        CollectConfiguredPaths(configuration, string.Empty, configuredPaths);
        return configuredPaths;
    }

    private static void CollectConfiguredPaths(
        IConfiguration configuration,
        string currentPath,
        ISet<string> configuredPaths)
    {
        var children = configuration.GetChildren().ToList();
        if (children.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                configuredPaths.Add(currentPath);
            }

            return;
        }

        foreach (var child in children)
        {
            var childPath = string.IsNullOrWhiteSpace(currentPath)
                ? child.Key
                : $"{currentPath}:{child.Key}";
            CollectConfiguredPaths(child, childPath, configuredPaths);
        }
    }

    private static object? SerializeValue(
        object? value,
        string currentPath,
        PropertyInfo? sourceProperty,
        ISet<string> configuredPaths)
    {
        if (ShouldSkipValue(sourceProperty, value, currentPath, configuredPaths))
        {
            return null;
        }

        return value switch
        {
            null => null,
            IConfiguration configuration => SerializeDictionary(
                configuration.GetDictionaryFromConfiguration(),
                currentPath,
                configuredPaths),
            IDictionary dictionary => SerializeDictionary(dictionary, currentPath, configuredPaths),
            IEnumerable enumerable when value is not string => SerializeEnumerable(enumerable, currentPath, configuredPaths),
            object nonNullValue when IsScalar(nonNullValue.GetType()) => nonNullValue,
            object nonNullObject => SerializeObject(nonNullObject, currentPath, configuredPaths)
        };
    }

    private static Dictionary<string, object?> SerializeDictionary(
        IDictionary dictionary,
        string currentPath,
        ISet<string> configuredPaths)
    {
        var serializedDictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (DictionaryEntry item in dictionary)
        {
            var key = item.Key?.ToString();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var childPath = $"{currentPath}:{key}";
            var serializedValue = SerializeValue(item.Value, childPath, null, configuredPaths);
            if (serializedValue == null || IsEmptyContainer(serializedValue))
            {
                continue;
            }

            serializedDictionary[key] = serializedValue;
        }

        return serializedDictionary;
    }

    private static List<object?> SerializeEnumerable(
        IEnumerable enumerable,
        string currentPath,
        ISet<string> configuredPaths)
    {
        var serializedList = new List<object?>();
        var itemIndex = 0;
        foreach (var item in enumerable)
        {
            var itemPath = $"{currentPath}:{itemIndex}";
            var serializedItem = SerializeValue(item, itemPath, null, configuredPaths);
            if (serializedItem != null && !IsEmptyContainer(serializedItem))
            {
                serializedList.Add(serializedItem);
            }

            itemIndex++;
        }

        return serializedList;
    }

    private static Dictionary<string, object?> SerializeObject(
        object value,
        string currentPath,
        ISet<string> configuredPaths)
    {
        var serializedObject = new Dictionary<string, object?>(StringComparer.Ordinal);
        var properties = value.GetType()
            .GetProperties(ConfigPropertyBindingFlags)
            .Where(ShouldSerializeProperty)
            .OrderBy(property => property.MetadataToken);

        foreach (var property in properties)
        {
            var propertyPath = $"{currentPath}:{property.Name}";
            var propertyValue = property.GetValue(value);
            var serializedValue = SerializeValue(propertyValue, propertyPath, property, configuredPaths);
            if (serializedValue == null || IsEmptyContainer(serializedValue))
            {
                continue;
            }

            serializedObject[property.Name] = serializedValue;
        }

        return serializedObject;
    }

    private static bool ShouldSerializeProperty(PropertyInfo property)
    {
        if (property.GetIndexParameters().Length != 0)
        {
            return false;
        }

        var getter = property.GetMethod ?? property.GetGetMethod(nonPublic: true);
        if (getter == null || getter.IsStatic)
        {
            return false;
        }

        if (property.Name is "EqualityContract")
        {
            return false;
        }

        if (typeof(Delegate).IsAssignableFrom(property.PropertyType) ||
            property.PropertyType.IsPointer ||
            property.PropertyType.IsByRef)
        {
            return false;
        }

        return true;
    }

    private static bool ShouldSkipValue(
        PropertyInfo? property,
        object? value,
        string currentPath,
        ISet<string> configuredPaths)
    {
        if (value == null)
        {
            return true;
        }

        if (value is string stringValue && string.IsNullOrWhiteSpace(stringValue))
        {
            return true;
        }

        if (value is IEnumerable enumerable and not string)
        {
            return !enumerable.Cast<object?>().Any(item => item != null);
        }

        if (property == null || ShouldAlwaysInclude(property))
        {
            return false;
        }

        if (property.GetCustomAttributesData().Any(attribute =>
                attribute.AttributeType.FullName == typeof(System.ComponentModel.DefaultValueAttribute).FullName) &&
            IsDefaultValue(property, value) &&
            !IsExplicitlyConfigured(currentPath, configuredPaths))
        {
            return true;
        }

        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (propertyType.IsValueType &&
            Equals(value, Activator.CreateInstance(propertyType)) &&
            !IsExplicitlyConfigured(currentPath, configuredPaths))
        {
            return true;
        }

        return false;
    }

    private static bool IsDefaultValue(PropertyInfo property, object value)
    {
        var defaultValueAttribute = property
            .GetCustomAttributes(inherit: true)
            .FirstOrDefault(attribute =>
                attribute.GetType().FullName == typeof(System.ComponentModel.DefaultValueAttribute).FullName);
        if (defaultValueAttribute == null)
        {
            return false;
        }

        var defaultValue = defaultValueAttribute.GetType()
            .GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(defaultValueAttribute);
        if (defaultValue == null)
        {
            return value == null;
        }

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        try
        {
            var comparableDefaultValue = targetType.IsEnum
                ? Enum.Parse(targetType, defaultValue.ToString()!, ignoreCase: false)
                : Convert.ChangeType(defaultValue, targetType);
            return Equals(value, comparableDefaultValue);
        }
        catch
        {
            return Equals(value, defaultValue);
        }
    }

    private static bool IsExplicitlyConfigured(string currentPath, ISet<string> configuredPaths)
    {
        return configuredPaths.Contains(currentPath) ||
               configuredPaths.Any(path => path.StartsWith($"{currentPath}:", StringComparison.Ordinal));
    }

    private static bool ShouldAlwaysInclude(PropertyInfo property)
    {
        return property.Name == "StatusesToReport";
    }

    private static object? NormalizeValue(object? value)
    {
        return value switch
        {
            null => null,
            IDictionary dictionary => NormalizeDictionary(dictionary),
            IEnumerable enumerable when value is not string => NormalizeEnumerable(enumerable),
            _ => value
        };
    }

    private static Dictionary<string, object?> NormalizeDictionary(IDictionary dictionary)
    {
        var normalizedDictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (DictionaryEntry item in dictionary)
        {
            if (string.IsNullOrWhiteSpace(item.Key?.ToString()))
            {
                continue;
            }

            normalizedDictionary[item.Key!.ToString()!] = NormalizeValue(item.Value);
        }

        return normalizedDictionary;
    }

    private static List<object?> NormalizeEnumerable(IEnumerable enumerable)
    {
        var normalizedList = new List<object?>();
        foreach (var item in enumerable)
        {
            var normalizedItem = NormalizeValue(item);
            if (normalizedItem == null)
            {
                continue;
            }

            normalizedList.Add(normalizedItem);
        }

        return normalizedList;
    }

    private static object? FilterNamedSection(object? sectionValue, ISet<string>? includedNames)
    {
        if (includedNames == null || sectionValue is not IList sectionList)
        {
            return sectionValue;
        }

        var filteredItems = new List<object?>();
        foreach (var item in sectionList)
        {
            if (item is not IDictionary dictionary || !TryGetName(dictionary, out var itemName))
            {
                continue;
            }

            if (includedNames.Contains(itemName))
            {
                filteredItems.Add(item);
            }
        }

        return filteredItems;
    }

    private static object? AugmentAssertionStatuses(
        object? sectionValue,
        IDictionary<string, IReadOnlyList<string>>? assertionStatusesToReport)
    {
        if (assertionStatusesToReport == null || sectionValue is not IList assertionList)
        {
            return sectionValue;
        }

        var updatedAssertions = new List<object?>();
        foreach (var item in assertionList)
        {
            if (item is not IDictionary dictionary || !TryGetName(dictionary, out var assertionName))
            {
                updatedAssertions.Add(item);
                continue;
            }

            if (!assertionStatusesToReport.TryGetValue(assertionName, out var statuses))
            {
                updatedAssertions.Add(item);
                continue;
            }

            var updatedAssertion = new Dictionary<string, object?>(StringComparer.Ordinal);
            var replacedExistingStatuses = false;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (string.IsNullOrWhiteSpace(entry.Key?.ToString()))
                {
                    continue;
                }

                var key = entry.Key!.ToString()!;
                if (key == "StatusesToReport")
                {
                    updatedAssertion[key] = statuses.ToList();
                    replacedExistingStatuses = true;
                    continue;
                }

                updatedAssertion[key] = NormalizeValue(entry.Value);
            }

            if (!replacedExistingStatuses)
            {
                updatedAssertion["StatusesToReport"] = statuses.ToList();
            }

            updatedAssertions.Add(updatedAssertion);
        }

        return updatedAssertions;
    }

    private static bool TryGetName(IDictionary dictionary, out string name)
    {
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key?.ToString() != "Name" || entry.Value is not string stringValue ||
                string.IsNullOrWhiteSpace(stringValue))
            {
                continue;
            }

            name = stringValue;
            return true;
        }

        name = string.Empty;
        return false;
    }

    private static bool IsScalar(Type type)
    {
        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;
        return effectiveType.IsPrimitive ||
               effectiveType.IsEnum ||
               effectiveType == typeof(string) ||
               effectiveType == typeof(decimal) ||
               effectiveType == typeof(DateTime) ||
               effectiveType == typeof(DateTimeOffset) ||
               effectiveType == typeof(TimeSpan) ||
               effectiveType == typeof(Guid);
    }

    private static bool IsEmptyContainer(object value)
    {
        return value switch
        {
            IDictionary dictionary => dictionary.Count == 0,
            ICollection collection => collection.Count == 0,
            _ => false
        };
    }
}
