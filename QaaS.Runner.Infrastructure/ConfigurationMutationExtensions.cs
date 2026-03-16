using System.Collections;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using QaaS.Framework.Configurations.ConfigurationBindingUtils;

namespace QaaS.Runner.Infrastructure;

/// <summary>
/// Centralizes runner configuration mutation semantics so builders can apply partial updates consistently.
/// </summary>
public static class ConfigurationMutationExtensions
{
    /// <summary>
    /// Merges a protocol configuration into the current one when both share a compatible runtime type.
    /// When no current configuration exists, the incoming configuration becomes the current value.
    /// </summary>
    public static TConfiguration UpdateConfiguration<TConfiguration>(
        this TConfiguration? currentConfiguration,
        TConfiguration incomingConfiguration)
        where TConfiguration : class
    {
        ArgumentNullException.ThrowIfNull(incomingConfiguration);

        if (currentConfiguration == null)
            return incomingConfiguration;

        if (currentConfiguration.GetType() != incomingConfiguration.GetType())
            return incomingConfiguration;

        MergeIntoCurrent(currentConfiguration, incomingConfiguration);
        return currentConfiguration;
    }

    /// <summary>
    /// Applies an object-shaped configuration update onto an <see cref="IConfiguration"/> tree.
    /// This is used by hook builders whose configuration is stored as raw key-value configuration.
    /// </summary>
    public static IConfiguration UpdateConfiguration(
        this IConfiguration? currentConfiguration,
        object incomingConfiguration)
    {
        ArgumentNullException.ThrowIfNull(incomingConfiguration);

        return (currentConfiguration ?? new ConfigurationBuilder().Build())
            .BindConfigurationObjectToIConfiguration(incomingConfiguration);
    }

    private static void MergeIntoCurrent(object currentConfiguration, object incomingConfiguration)
    {
        var configurationType = incomingConfiguration.GetType();
        var defaultConfiguration = CreateDefaultConfiguration(configurationType);

        foreach (var property in GetMergeableProperties(configurationType))
        {
            var incomingValue = property.GetValue(incomingConfiguration);
            if (incomingValue == null)
                continue;

            var defaultValue = defaultConfiguration != null ? property.GetValue(defaultConfiguration) : null;
            if (AreEquivalentValues(incomingValue, defaultValue, property.PropertyType))
                continue;

            var currentValue = property.GetValue(currentConfiguration);
            if (!IsComplexType(property.PropertyType) ||
                currentValue == null ||
                currentValue.GetType() != incomingValue.GetType())
            {
                property.SetValue(currentConfiguration, incomingValue);
                continue;
            }

            MergeIntoCurrent(currentValue, incomingValue);
        }
    }

    private static IEnumerable<PropertyInfo> GetMergeableProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0);
    }

    private static bool IsComplexType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return !(underlyingType.IsPrimitive ||
                 underlyingType.IsEnum ||
                 underlyingType == typeof(decimal) ||
                 underlyingType == typeof(DateTime) ||
                 underlyingType == typeof(DateTimeOffset) ||
                 underlyingType == typeof(TimeSpan) ||
                 underlyingType == typeof(Guid) ||
                 underlyingType == typeof(Uri) ||
                 typeof(IEnumerable).IsAssignableFrom(underlyingType) ||
                 underlyingType == typeof(string));
    }

    private static object? CreateDefaultConfiguration(Type type)
    {
        try
        {
            return Activator.CreateInstance(type);
        }
        catch
        {
            return null;
        }
    }

    private static bool AreEquivalentValues(object? left, object? right, Type type)
    {
        if (left == null || right == null)
            return left == right;

        if (type == typeof(string))
            return string.Equals((string)left, (string)right, StringComparison.Ordinal);

        if (left is IEnumerable leftEnumerable && right is IEnumerable rightEnumerable && type != typeof(string))
            return AreEquivalentEnumerables(leftEnumerable, rightEnumerable);

        if (!IsComplexType(type))
            return Equals(left, right);

        return GetMergeableProperties(type)
            .All(property => AreEquivalentValues(
                property.GetValue(left),
                property.GetValue(right),
                property.PropertyType));
    }

    private static bool AreEquivalentEnumerables(IEnumerable left, IEnumerable right)
    {
        var leftEnumerator = left.GetEnumerator();
        var rightEnumerator = right.GetEnumerator();
        try
        {
            while (true)
            {
                var leftMoved = leftEnumerator.MoveNext();
                var rightMoved = rightEnumerator.MoveNext();

                if (leftMoved != rightMoved)
                    return false;

                if (!leftMoved)
                    return true;

                if (!Equals(leftEnumerator.Current, rightEnumerator.Current))
                    return false;
            }
        }
        finally
        {
            (leftEnumerator as IDisposable)?.Dispose();
            (rightEnumerator as IDisposable)?.Dispose();
        }
    }
}
