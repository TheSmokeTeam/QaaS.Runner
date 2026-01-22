using NUnit.Framework;
using QaaS.Framework.Configurations.CommonConfigurationObjects;
using S3Config = QaaS.Runner.Storage.ConfigurationObjects.StorageConfigs.S3Config;

namespace QaaS.Runner.Storage.Tests;

public class CaseStorageHandlerTests
{
    [Test]
    [TestCase("prefix", null, "prefix")]
    [TestCase("prefix", "case", "prefixcase/")]
    [TestCase("prefix", "/case", "prefix_case/")]
    [TestCase("prefix", "\\case", "prefix_case/")]
    [TestCase("", "case", "case/")]
    public void TestHandleCaseWithS3_CallFunction_ShouldReturnExpectedResult(
        string prefix, string? caseName, string expectedResult)
    {
        // Arrange
        var config = new S3Config { Prefix = prefix };

        // Act
        var result = CaseStorageHandler.HandleCaseWithS3(config, caseName);

        // Assert
        Assert.AreEqual(expectedResult, result);
    }

    [Test]
    [TestCase("dir", null, "dir")]
    [TestCase("dir", "case", "dir/case")]
    [TestCase("", "case", "case")]
    public void TestHandleCaseWithFileSystem_CallFunction_ShouldReturnExpectedResult(
        string path, string? caseName, string expectedResult)
    {
        // Arrange
        var config = new FilesInFileSystemConfig { Path = path };

        // Act
        var result = CaseStorageHandler.HandleCaseWithFileSystem(config, caseName);

        // Assert
        Assert.AreEqual(expectedResult.Replace('/', Path.DirectorySeparatorChar),
            result);
    }
}