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
    [TestCase("...", ExpectedResult = "_")]
    [TestCase("..", ExpectedResult = "_")]
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
    public void NormalizeRelativePath_WithBackslashSeparators_ReturnsNormalizedPath()
    {
        var result = FileSystemExtensions.NormalizeRelativePath(@"folder\sub\file.json");

        Assert.That(result, Is.EqualTo(Path.Combine("folder", "sub", "file.json")));
    }

    [Test]
    public void NormalizeRelativePath_WithWhitespace_ReturnsEmptyPath()
    {
        var result = FileSystemExtensions.NormalizeRelativePath("   ");

        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void NormalizeRelativePath_WithTraversalSegments_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => FileSystemExtensions.NormalizeRelativePath("../secret.json"));
    }

    [Test]
    public void NormalizeRelativePath_WithRootedPath_ThrowsInvalidOperationException()
    {
        var rootedPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "rooted.json"));

        Assert.Throws<InvalidOperationException>(() => FileSystemExtensions.NormalizeRelativePath(rootedPath));
    }

    [Test]
    public void NormalizeRelativePath_WithInvalidSegment_SanitizesEachSegment()
    {
        var result = FileSystemExtensions.NormalizeRelativePath("folder/repor?.json");

        Assert.That(result, Is.EqualTo(Path.Combine("folder", "repor_.json")));
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

    [Test]
    public void CombineUnderRoot_WithNoSegments_ReturnsNormalizedRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = FileSystemExtensions.CombineUnderRoot(root);

        Assert.That(result, Is.EqualTo(Path.GetFullPath(root)));
    }

    [Test]
    public void CombineUnderRoot_WithBlankRoot_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => FileSystemExtensions.CombineUnderRoot(" ", "child"));
    }

    [Test]
    public void CombineUnderRoot_WithNullSegments_IgnoresThem()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = FileSystemExtensions.CombineUnderRoot(root, null, "", "child");

        Assert.That(result, Is.EqualTo(Path.Combine(Path.GetFullPath(root), "child")));
    }
}
