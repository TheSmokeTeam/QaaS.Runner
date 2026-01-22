using NUnit.Framework;

namespace QaaS.Runner.Infrastructure.Tests;

public class FileSystemExtensionsTests
{
    [Test]
    [TestCase("ValidDirectoryName", ExpectedResult = "ValidDirectoryName")]
    [TestCase("http//Invalid_Directory_Name", ExpectedResult = "http__Invalid_Directory_Name")]
    [TestCase(null, ExpectedResult = null)]
    [TestCase("", ExpectedResult = "")]
    public string? TestMakeValidDirectoryName_CallFunction_ShouldReturnExpectedResult(string? name)
    {
        return FileSystemExtensions.MakeValidDirectoryName(name);
    }
}