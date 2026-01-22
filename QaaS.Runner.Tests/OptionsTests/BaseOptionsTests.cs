using Microsoft.Extensions.Logging;
using NUnit.Framework;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Runner.Options;

namespace QaaS.Runner.Tests.OptionsTests;

public class BaseOptionsTests
{
    private static TestCaseData[] _invalidReferenceLists =
    {
        new TestCaseData(new List<string> { "ref.yaml" }).SetName("Only yamls"),
        new TestCaseData(new List<string> { "keyword", "keyword", "ref.yaml" })
            .SetName("empty keyword at start with another valid keyword after it"),
        new TestCaseData(new List<string> { "keyword" }).SetName("1 empty keyword alone"),
        new TestCaseData(new List<string> { "keyword", "ref.yaml", "keyword" }).SetName("empty keyword at end")
    };

    [Test]
    [TestCase(new string[] { }, 0)]
    [TestCase(new[] { "keyword", "file1.yaml" }, 1)]
    [TestCase(new[] { "keyword", "file1.yaml", "file2.yaml" }, 1)]
    [TestCase(new[] { "keyword", "file1.yaml", "keyword2", "file2.yaml" }, 2)]
    [TestCase(new[] { "keyword", "file1.yaml", "keyword2", "file1.yaml", "keyword3", "file2.yaml" }, 3)]
    public void TestGetParsedPushReferences_CallFunctionWithPushReferencesList_ReturnsExpectedNumberOfReferenceConfigs(
        string[] pushReferences, int expectedCount)
    {
        // Arrange
        var mock = new MockOptions
        {
            PushReferences = pushReferences.ToList()
        };

        // Act
        var result = mock.GetParsedPushReferences();

        // Assert
        Assert.That(result.Count(), Is.EqualTo(expectedCount));
    }

    [Test]
    [TestCaseSource(nameof(_invalidReferenceLists))]
    public void TestGetParsedPushReferences_CallFunctionWithInvalidReferenceList_ShouldThrowException(
        List<string> invalidReferenceList)
    {
        // Arrange
        var mock = new MockOptions
        {
            PushReferences = invalidReferenceList
        };

        // Act + Assert
        var exception = Assert.Throws<ArgumentException>(() => mock.GetParsedPushReferences().ToList());
        Globals.Logger.LogInformation("Exception is {Exception}", exception);
    }

    private record MockOptions : BaseOptions
    {
        public override ExecutionType GetExecutionType()
        {
            return ExecutionType.Run;
        }
    }
}