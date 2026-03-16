using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using NUnit.Framework;

namespace QaaS.Runner.Tests;

[TestFixture]
public class RunnerValidationUtilsTests
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Enum)]
    private sealed class AlwaysInvalidAttribute(string message) : ValidationAttribute(message)
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            return new ValidationResult(ErrorMessage);
        }
    }

    [AlwaysInvalid("Invalid enum value.")]
    private enum InvalidEnum
    {
        Value
    }

    private sealed class NestedNode
    {
        [Required]
        public string? Name { get; set; }
    }

    [AlwaysInvalid("Container is invalid.")]
    private sealed class ValidationContainer
    {
        [System.ComponentModel.Description("Nested dictionary")]
        public IDictionary<string, NestedNode> DictionaryItems { get; init; } = new Dictionary<string, NestedNode>();

        [DefaultValue(null)]
        public IList<NestedNode> ListItems { get; init; } = [];

        [Required]
        internal string? HiddenValue { get; init; }
    }

    private sealed class PropertyValidationContainer
    {
        [Required]
        internal string? HiddenRequired { get; init; }

        public string this[int index] => $"value-{index}";
    }

    private sealed class ThrowingGetterContainer
    {
        [System.ComponentModel.Description("Explodes when read")]
        public string ExplodingValue => throw new InvalidOperationException("boom");
    }

    [Test]
    public void TryValidateProperties_IgnoresMissingAndIndexerProperties_AndValidatesNonPublicMembers()
    {
        var results = new List<ValidationResult>();

        var isValid = RunnerValidationUtils.TryValidateProperties(new PropertyValidationContainer(), results,
            "HiddenRequired", "Item", "MissingProperty");

        Assert.That(isValid, Is.False);
        Assert.That(results.Select(result => result.ErrorMessage),
            Has.One.EqualTo("The HiddenRequired field is required."));
    }

    [Test]
    public void TryValidateObjectRecursive_WithNullObject_ReturnsTrueWithoutResults()
    {
        var results = new List<ValidationResult>();

        var isValid = RunnerValidationUtils.TryValidateObjectRecursive(null, results);

        Assert.Multiple(() =>
        {
            Assert.That(isValid, Is.True);
            Assert.That(results, Is.Empty);
        });
    }

    [Test]
    public void TryValidateObjectRecursive_ValidatesTerminalTypesAndPrefixesParentPath()
    {
        var results = new List<ValidationResult>();

        var isValid = RunnerValidationUtils.TryValidateObjectRecursive(InvalidEnum.Value, results, "Root:EnumValue");

        Assert.That(isValid, Is.False);
        Assert.That(results.Select(result => result.ErrorMessage),
            Has.One.EqualTo("Root:EnumValue - Invalid enum value."));
    }

    [Test]
    public void TryValidateObjectRecursive_ValidatesClassLevelNonPublicDictionaryAndListBranches()
    {
        var container = new ValidationContainer
        {
            DictionaryItems = new Dictionary<string, NestedNode>
            {
                ["alpha"] = new()
            },
            ListItems =
            [
                new NestedNode()
            ],
            HiddenValue = null
        };
        var results = new List<ValidationResult>();

        var isValid = RunnerValidationUtils.TryValidateObjectRecursive(container, results);

        Assert.That(isValid, Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(results.Select(result => result.ErrorMessage),
                Has.One.EqualTo("Container is invalid."));
            Assert.That(results.Select(result => result.ErrorMessage),
                Has.One.EqualTo("The HiddenValue field is required."));
            Assert.That(results.Select(result => result.ErrorMessage),
                Has.One.EqualTo("DictionaryItems:alpha - The Name field is required."));
            Assert.That(results.Select(result => result.ErrorMessage),
                Has.One.EqualTo("ListItems:0 - The Name field is required."));
        });
    }

    [Test]
    public void TryValidateObjectRecursive_WhenGetterThrows_RethrowsTargetInvocationException()
    {
        var results = new List<ValidationResult>();

        Assert.Throws<TargetInvocationException>(() =>
            RunnerValidationUtils.TryValidateObjectRecursive(new ThrowingGetterContainer(), results));
    }
}
