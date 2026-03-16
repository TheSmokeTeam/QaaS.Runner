using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using System.Reflection;
using QaaS.Framework.Configurations;

namespace QaaS.Runner;

internal static class RunnerValidationUtils
{
    public static bool TryValidateProperties(object obj, List<ValidationResult> results, params string[] propertyNames)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(propertyNames);

        var validationResults = new List<ValidationResult>();
        var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (var propertyName in propertyNames.Distinct(StringComparer.Ordinal))
        {
            var property = obj.GetType().GetProperty(propertyName, bindingFlags);
            if (property == null || property.GetIndexParameters().Length > 0 ||
                !TryGetPropertyValue(obj, property, out var propertyValue))
            {
                continue;
            }

            var validationContext = new ValidationContext(obj, null, null)
            {
                MemberName = property.Name
            };

            foreach (var validationAttribute in property.GetCustomAttributes<ValidationAttribute>())
            {
                var result = validationAttribute.GetValidationResult(propertyValue, validationContext);
                if (result != ValidationResult.Success && result != null)
                {
                    validationResults.Add(result);
                }
            }
        }

        results.AddRange(DistinctValidationResults(validationResults));
        return !validationResults.Any();
    }

    public static bool TryValidateObjectRecursive(object? obj, List<ValidationResult> results, string parentPath = "",
        BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
    {
        if (obj == null)
        {
            return true;
        }

        if (IsTerminalType(obj.GetType()))
        {
            var terminalResults = new List<ValidationResult>();
            _ = TryValidateCurrentObject(obj, terminalResults, bindingFlags);
            results.AddRange(terminalResults.Select(result =>
            {
                var trimmedParentPath = parentPath.TrimStart(ConfigurationConstants.PathSeparator.ToCharArray());
                var parentPrefix = trimmedParentPath.Length == 0 ? string.Empty : $"{trimmedParentPath} - ";
                result.ErrorMessage = $"{parentPrefix}{result.ErrorMessage}";
                return result;
            }));
            return !terminalResults.Any();
        }

        var localResults = new List<ValidationResult>();
        var isValid = TryValidateCurrentObject(obj, localResults, bindingFlags);

        results.AddRange(localResults.Select(result =>
        {
            var trimmedParentPath = parentPath.TrimStart(ConfigurationConstants.PathSeparator.ToCharArray());
            var parentPrefix = trimmedParentPath.Length == 0 ? string.Empty : $"{trimmedParentPath} - ";
            result.ErrorMessage = $"{parentPrefix}{result.ErrorMessage}";
            return result;
        }));

        var properties = obj.GetType()
            .GetProperties(bindingFlags)
            .Where(property => property.GetIndexParameters().Length == 0 &&
                               property.PropertyType != obj.GetType() &&
                               ShouldInspectProperty(property));

        foreach (var property in properties)
        {
            if (!TryGetPropertyValue(obj, property, out var value))
            {
                continue;
            }

            var propertyPath = $"{parentPath}{ConfigurationConstants.PathSeparator}{property.Name}";

            if (value is IEnumerable enumerableValue && value is not string)
            {
                if (value is IDictionary dictionary)
                {
                    foreach (var key in dictionary.Keys)
                    {
                        var entryPath = $"{propertyPath}{ConfigurationConstants.PathSeparator}{key}";
                        var entry = dictionary[key];
                        if (entry != null && !TryValidateObjectRecursive(entry, results, entryPath, bindingFlags))
                        {
                            isValid = false;
                        }
                    }
                }
                else
                {
                    var index = 0;
                    foreach (var item in enumerableValue)
                    {
                        var itemPath = $"{propertyPath}{ConfigurationConstants.PathSeparator}{index}";
                        if (item != null && !TryValidateObjectRecursive(item, results, itemPath, bindingFlags))
                        {
                            isValid = false;
                        }

                        index++;
                    }
                }
            }
            else if (value != null && !IsTerminalType(value.GetType()) &&
                     !TryValidateObjectRecursive(value, results, propertyPath, bindingFlags))
            {
                isValid = false;
            }
        }

        return isValid;
    }

    private static bool TryValidateCurrentObject(object obj, List<ValidationResult> results, BindingFlags bindingFlags)
    {
        var validationResults = new List<ValidationResult>();
        var objectType = obj.GetType();

        if (IsTerminalType(objectType))
        {
            var validationContext = new ValidationContext(obj, null, null)
            {
                MemberName = string.Empty
            };

            foreach (var validationAttribute in objectType.GetCustomAttributes<ValidationAttribute>())
            {
                var result = validationAttribute.GetValidationResult(obj, validationContext);
                if (result != ValidationResult.Success && result != null)
                {
                    validationResults.Add(result);
                }
            }
        }
        else
        {
            _ = Validator.TryValidateObject(obj, new ValidationContext(obj), validationResults, true);
            var objectValidationContext = new ValidationContext(obj, null, null)
            {
                MemberName = string.Empty
            };

            foreach (var validationAttribute in objectType.GetCustomAttributes<ValidationAttribute>())
            {
                var result = validationAttribute.GetValidationResult(obj, objectValidationContext);
                if (result != ValidationResult.Success && result != null)
                {
                    validationResults.Add(result);
                }
            }

            if ((bindingFlags & BindingFlags.NonPublic) != 0)
            {
                foreach (var property in objectType.GetProperties(bindingFlags)
                             .Where(property => property.GetMethod?.IsPublic != true))
                {
                    var validationAttributes = property.GetCustomAttributes<ValidationAttribute>().ToArray();
                    if (property.GetIndexParameters().Length > 0 || validationAttributes.Length == 0 ||
                        !TryGetPropertyValue(obj, property, out var propertyValue))
                    {
                        continue;
                    }

                    var validationContext = new ValidationContext(obj, null, null)
                    {
                        MemberName = property.Name
                    };

                    foreach (var validationAttribute in validationAttributes)
                    {
                        var result = validationAttribute.GetValidationResult(propertyValue, validationContext);
                        if (result != ValidationResult.Success && result != null)
                        {
                            validationResults.Add(result);
                        }
                    }
                }
            }
        }

        results.AddRange(DistinctValidationResults(validationResults));
        return !validationResults.Any();
    }

    private static IEnumerable<ValidationResult> DistinctValidationResults(IEnumerable<ValidationResult> validationResults)
    {
        return validationResults
            .GroupBy(result => new
            {
                Message = result.ErrorMessage ?? string.Empty,
                Members = string.Join("|", result.MemberNames.OrderBy(member => member, StringComparer.Ordinal))
            })
            .Select(group => group.First());
    }

    private static bool TryGetPropertyValue(object instance, PropertyInfo property, out object? value)
    {
        try
        {
            value = property.GetValue(instance);
            return true;
        }
        catch (TargetInvocationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is MethodAccessException or ArgumentException)
        {
            var getter = property.GetGetMethod(nonPublic: true);
            if (getter == null)
            {
                value = null;
                return false;
            }

            value = getter.Invoke(instance, null);
            return true;
        }
    }

    private static bool IsTerminalType(Type type)
    {
        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;

        return effectiveType.IsPrimitive
               || effectiveType.IsEnum
               || effectiveType == typeof(string)
               || effectiveType == typeof(decimal)
               || effectiveType == typeof(DateTime)
               || effectiveType == typeof(DateTimeOffset)
               || effectiveType == typeof(TimeSpan)
               || effectiveType == typeof(Guid)
               || effectiveType == typeof(Uri)
               || effectiveType == typeof(Type)
               || typeof(Delegate).IsAssignableFrom(effectiveType)
               || typeof(System.Reflection.MemberInfo).IsAssignableFrom(effectiveType)
               || typeof(System.Reflection.Assembly).IsAssignableFrom(effectiveType)
               || effectiveType == typeof(IntPtr)
               || effectiveType == typeof(UIntPtr)
               || effectiveType.IsPointer
               || effectiveType.IsByRef;
    }

    private static bool ShouldInspectProperty(PropertyInfo property)
    {
        return property.GetCustomAttributes<ValidationAttribute>().Any()
               || property.GetCustomAttributes<DescriptionAttribute>().Any()
               || property.GetCustomAttributes<DefaultValueAttribute>().Any();
    }
}
