using System;
using System.IO;
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

    [Test]
    [TestCase("report?.json", ExpectedResult = "report_.json")]
    [TestCase("", ExpectedResult = "")]
    [TestCase(null, ExpectedResult = null)]
    public string? TestMakeValidFileName_CallFunction_ShouldReturnExpectedResult(string? name)
    {
        return FileSystemExtensions.MakeValidFileName(name);
    }

    [Test]
    public void NormalizeRelativePath_WithNestedRelativePath_ReturnsNormalizedPath()
    {
        var result = FileSystemExtensions.NormalizeRelativePath("folder/sub/file.json");

        Assert.That(result, Is.EqualTo(Path.Combine("folder", "sub", "file.json")));
    }

    [Test]
    public void NormalizeRelativePath_WithTraversalSegments_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => FileSystemExtensions.NormalizeRelativePath("../secret.json"));
    }

    [Test]
    public void CombineUnderRoot_WithChildSegments_ReturnsPathUnderRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = FileSystemExtensions.CombineUnderRoot(root, "child", "file.json");

        Assert.That(result, Is.EqualTo(Path.Combine(Path.GetFullPath(root), "child", "file.json")));
    }

    [Test]
    public void CombineUnderRoot_WithEscapingSegments_ThrowsInvalidOperationException()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.Throws<InvalidOperationException>(() =>
            FileSystemExtensions.CombineUnderRoot(root, "..", "outside.json"));
    }
}
