using System.Collections;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using QaaS.Framework.Configurations.ConfigurationBindingUtils;
using YamlDotNet.Serialization;

namespace QaaS.Runner.Infrastructure;

/// <summary>
/// Renders a stable YAML view of the effective runner configuration, combining source configuration with runtime-resolved builder values.
/// </summary>
public static class ConfigurationTemplateRenderer
{
    private static readonly BindingFlags ConfigPropertyBindingFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// Serializes the effective runner configuration in a deterministic section order, preserving explicitly configured defaults
    /// and injecting runtime-only assertion status settings when needed.
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
        var hasFallbackValue = TryGetAliasedValue(sectionName, fallbackSections, out var fallbackValue);
        var hasRootValue = TryGetAliasedValue(sectionName, rootSections, out var rootValue);
        if (hasFallbackValue)
        {
            sectionValue = hasRootValue
                ? PreserveSourceStructure(rootValue, fallbackValue)
                : fallbackValue;
            return true;
        }

        sectionValue = hasRootValue ? rootValue : null;
        return hasRootValue;
    }

    /// <summary>
    /// Looks up a section value using the current section name and any supported aliases.
    /// </summary>
    private static bool TryGetAliasedValue(
        string sectionName,
        IReadOnlyDictionary<string, object?> sections,
        out object? sectionValue)
    {
        foreach (var key in ResolveSectionAliases(sectionName))
        {
            if (sections.TryGetValue(key, out sectionValue))
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

    /// <summary>
    /// Reuses the source configuration's sparse numeric-key structure when the rendered fallback value would otherwise
    /// collapse it into a dense list.
    /// </summary>
    private static object? PreserveSourceStructure(object? sourceValue, object? renderedValue)
    {
        if (renderedValue == null)
        {
            return sourceValue;
        }

        if (sourceValue is IDictionary sourceDictionary &&
            renderedValue is IList renderedList &&
            IsNumericKeyDictionary(sourceDictionary))
        {
            return RehydrateSparseIndexedDictionary(sourceDictionary, renderedList);
        }

        if (sourceValue is IDictionary sourceObject &&
            renderedValue is IDictionary renderedObject)
        {
            return MergeDictionariesPreservingSourceStructure(sourceObject, renderedObject);
        }

        return renderedValue;
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

    /// <summary>
    /// Merges object dictionaries while preserving sparse list-shaped dictionaries from the source configuration.
    /// </summary>
    private static Dictionary<string, object?> MergeDictionariesPreservingSourceStructure(
        IDictionary sourceDictionary,
        IDictionary renderedDictionary)
    {
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        var renderedEntries = GetDictionaryEntries(renderedDictionary)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

        foreach (var sourceEntry in GetDictionaryEntries(sourceDictionary))
        {
            merged[sourceEntry.Key] = renderedEntries.TryGetValue(sourceEntry.Key, out var renderedValue)
                ? PreserveSourceStructure(sourceEntry.Value, renderedValue)
                : sourceEntry.Value;
        }

        foreach (var renderedEntry in renderedEntries)
        {
            if (!merged.ContainsKey(renderedEntry.Key))
            {
                merged[renderedEntry.Key] = renderedEntry.Value;
            }
        }

        return merged;
    }

    /// <summary>
    /// Restores sparse numeric keys for list-like sections by matching rendered items back to the original source keys.
    /// </summary>
    private static Dictionary<string, object?> RehydrateSparseIndexedDictionary(
        IDictionary sourceDictionary,
        IList renderedList)
    {
        var sourceEntries = GetDictionaryEntries(sourceDictionary).ToList();
        var renderedItems = renderedList.Cast<object?>().ToList();
        var matchedRenderedIndices = MatchRenderedItemsByName(sourceEntries, renderedItems);
        var consumedRenderedIndices = matchedRenderedIndices.Values.ToHashSet();
        var remainingRenderedIndices = new Queue<int>(Enumerable.Range(0, renderedItems.Count)
            .Where(index => !consumedRenderedIndices.Contains(index)));
        var rehydrated = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var sourceEntry in sourceEntries)
        {
            if (matchedRenderedIndices.TryGetValue(sourceEntry.Key, out var renderedIndex))
            {
                rehydrated[sourceEntry.Key] =
                    PreserveSourceStructure(sourceEntry.Value, renderedItems[renderedIndex]);
                continue;
            }

            if (remainingRenderedIndices.Count == 0)
            {
                continue;
            }

            renderedIndex = remainingRenderedIndices.Dequeue();
            rehydrated[sourceEntry.Key] = PreserveSourceStructure(sourceEntry.Value, renderedItems[renderedIndex]);
        }

        var nextSparseIndex = sourceEntries
            .Select(entry => int.Parse(entry.Key, CultureInfo.InvariantCulture))
            .DefaultIfEmpty(-1)
            .Max();
        while (remainingRenderedIndices.Count > 0)
        {
            nextSparseIndex++;
            var renderedIndex = remainingRenderedIndices.Dequeue();
            rehydrated[nextSparseIndex.ToString(CultureInfo.InvariantCulture)] = renderedItems[renderedIndex];
        }

        return rehydrated;
    }

    /// <summary>
    /// Matches rendered list items to sparse source entries by item name when possible so filtered named sections keep
    /// their original indexes.
    /// </summary>
    private static Dictionary<string, int> MatchRenderedItemsByName(
        IReadOnlyList<KeyValuePair<string, object?>> sourceEntries,
        IReadOnlyList<object?> renderedItems)
    {
        var matchedIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var consumedRenderedIndices = new HashSet<int>();

        foreach (var sourceEntry in sourceEntries)
        {
            if (!TryGetItemName(sourceEntry.Value, out var sourceName))
            {
                continue;
            }

            for (var renderedIndex = 0; renderedIndex < renderedItems.Count; renderedIndex++)
            {
                if (consumedRenderedIndices.Contains(renderedIndex) ||
                    !TryGetItemName(renderedItems[renderedIndex], out var renderedName) ||
                    !string.Equals(sourceName, renderedName, StringComparison.Ordinal))
                {
                    continue;
                }

                matchedIndices[sourceEntry.Key] = renderedIndex;
                consumedRenderedIndices.Add(renderedIndex);
                break;
            }
        }

        return matchedIndices;
    }

    /// <summary>
    /// Returns <see langword="true" /> when every key in the dictionary is numeric, which indicates the dictionary
    /// represents a sparse list from the source configuration.
    /// </summary>
    private static bool IsNumericKeyDictionary(IDictionary dictionary)
    {
        var hasEntries = false;
        foreach (var entry in GetDictionaryEntries(dictionary))
        {
            hasEntries = true;
            if (!int.TryParse(entry.Key, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }
        }

        return hasEntries;
    }

    /// <summary>
    /// Enumerates dictionary entries while skipping blank keys.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, object?>> GetDictionaryEntries(IDictionary dictionary)
    {
        foreach (DictionaryEntry entry in dictionary)
        {
            var key = entry.Key?.ToString();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            yield return new KeyValuePair<string, object?>(key, entry.Value);
        }
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
        if (includedNames == null)
        {
            return sectionValue;
        }

        if (sectionValue is IList sectionList)
        {
            return FilterNamedListSection(sectionList, includedNames);
        }

        if (sectionValue is IDictionary sectionDictionary && IsNumericKeyDictionary(sectionDictionary))
        {
            return FilterNamedDictionarySection(sectionDictionary, includedNames);
        }

        return sectionValue;
    }

    /// <summary>
    /// Filters list-based named sections while preserving item order.
    /// </summary>
    private static List<object?> FilterNamedListSection(IList sectionList, ISet<string> includedNames)
    {
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

    /// <summary>
    /// Filters sparse numeric-key dictionaries without rewriting the original item indexes.
    /// </summary>
    private static Dictionary<string, object?> FilterNamedDictionarySection(
        IDictionary sectionDictionary,
        ISet<string> includedNames)
    {
        var filteredItems = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var entry in GetDictionaryEntries(sectionDictionary))
        {
            if (entry.Value is not IDictionary dictionary || !TryGetName(dictionary, out var itemName))
            {
                continue;
            }

            if (includedNames.Contains(itemName))
            {
                filteredItems[entry.Key] = entry.Value;
            }
        }

        return filteredItems;
    }

    private static object? AugmentAssertionStatuses(
        object? sectionValue,
        IDictionary<string, IReadOnlyList<string>>? assertionStatusesToReport)
    {
        if (assertionStatusesToReport == null)
        {
            return sectionValue;
        }

        if (sectionValue is IList assertionList)
        {
            return AugmentAssertionStatusList(assertionList, assertionStatusesToReport);
        }

        if (sectionValue is IDictionary assertionDictionary && IsNumericKeyDictionary(assertionDictionary))
        {
            return AugmentAssertionStatusDictionary(assertionDictionary, assertionStatusesToReport);
        }

        return sectionValue;
    }

    /// <summary>
    /// Updates assertion status lists for dense list-based sections.
    /// </summary>
    private static List<object?> AugmentAssertionStatusList(
        IList assertionList,
        IDictionary<string, IReadOnlyList<string>> assertionStatusesToReport)
    {
        var updatedAssertions = new List<object?>();
        foreach (var item in assertionList)
        {
            updatedAssertions.Add(UpdateAssertionStatuses(item, assertionStatusesToReport));
        }

        return updatedAssertions;
    }

    /// <summary>
    /// Updates assertion status lists for sparse numeric-key dictionary sections while keeping original indexes.
    /// </summary>
    private static Dictionary<string, object?> AugmentAssertionStatusDictionary(
        IDictionary assertionDictionary,
        IDictionary<string, IReadOnlyList<string>> assertionStatusesToReport)
    {
        var updatedAssertions = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var entry in GetDictionaryEntries(assertionDictionary))
        {
            updatedAssertions[entry.Key] = UpdateAssertionStatuses(entry.Value, assertionStatusesToReport);
        }

        return updatedAssertions;
    }

    /// <summary>
    /// Replaces or appends the runtime assertion statuses for a single assertion item.
    /// </summary>
    private static object? UpdateAssertionStatuses(
        object? item,
        IDictionary<string, IReadOnlyList<string>> assertionStatusesToReport)
    {
        if (item is not IDictionary dictionary || !TryGetName(dictionary, out var assertionName))
        {
            return item;
        }

        if (!assertionStatusesToReport.TryGetValue(assertionName, out var statuses))
        {
            return item;
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

        return updatedAssertion;
    }

    /// <summary>
    /// Tries to read the <c>Name</c> field from a rendered list item.
    /// </summary>
    private static bool TryGetItemName(object? item, out string name)
    {
        if (item is IDictionary dictionary)
        {
            return TryGetName(dictionary, out name);
        }

        name = string.Empty;
        return false;
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
