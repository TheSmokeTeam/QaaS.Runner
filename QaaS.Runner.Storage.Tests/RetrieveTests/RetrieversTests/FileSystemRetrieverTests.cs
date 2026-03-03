using System.IO.Abstractions;
using System.Reflection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using QaaS.Framework.Configurations.CommonConfigurationObjects;
using QaaS.Runner.Storage.ConfigurationObjects;

namespace QaaS.Runner.Storage.Tests.RetrieveTests.RetrieversTests;

public class FileSystemRetrieverTests
{
    private static readonly string TestDirectory = Path.Combine(Environment.CurrentDirectory, "FileSystemTests");

    private static readonly IFileSystem TestFileSystem = new FileSystem();

    [SetUp]
    [TearDown]
    public void DeleteTestDirectory()
    {
        if (!TestFileSystem.Directory.Exists(TestDirectory)) return;

        Globals.Logger.LogInformation("Deleting test directory {TestDirectory}", TestDirectory);
        TestFileSystem.Directory.Delete(TestDirectory, true);
    }

    private static IEnumerable<TestCaseData> TestLoadSerializedExternalDataSource()
    {
        var fakePrefix = new[] { "SomePath", "AtPath", "withPath" };
        var fileContent = new byte[] { 0 };

        yield return new TestCaseData(Array.Empty<string>(),
                new List<byte[]>(), fakePrefix, fileContent)
            .SetName("FileSystemRetrieverNoDataInFileSystemWithPrefix");

        yield return new TestCaseData(Array.Empty<string>(),
                new List<byte[]>(), Array.Empty<string>(), fileContent)
            .SetName("FileSystemRetrieverNoDataInFileSystemWithoutPrefix");

        yield return new TestCaseData(
                new[] { "test" },
                new List<byte[]> { fileContent }, fakePrefix, fileContent)
            .SetName("FileSystemRetrieverOneItemInFileSystemWithPrefix");

        yield return new TestCaseData(
                new[] { "test" },
                new List<byte[]> { fileContent }, Array.Empty<string>(), fileContent)
            .SetName("FileSystemRetrieverOneItemInFileSystemWithoutPrefix");

        yield return new TestCaseData(
                new[]
                {
                    "test",
                    "test2",
                    "test3"
                },
                new List<byte[]>
                {
                    fileContent, fileContent, fileContent
                }, fakePrefix, fileContent)
            .SetName("FileSystemRetrieverMultipleItemsInFileSystemWithPrefix");

        yield return new TestCaseData(
                new[]
                {
                    "test",
                    "test2",
                    "test3"
                },
                new List<byte[]>
                {
                    fileContent, fileContent, fileContent
                }, Array.Empty<string>(), fileContent)
            .SetName("FileSystemRetrieverMultipleItemsInFileSystemWithoutPrefix");
    }

    [Test]
    [TestCaseSource(nameof(TestLoadSerializedExternalDataSource))]
    public void
        TestRetrieveSerialized_CallFunctionWithMockedFileSystem_ShouldReturnExpectedOutput(string[] filesMockData,
            List<byte[]> expectedOutput, string[] prefixFolders, byte[] fileContent)
    {
        // Arrange
        var fullPrefixPath = TestDirectory;
        TestFileSystem.Directory.CreateDirectory(fullPrefixPath);
        // Create relevant directory and files
        foreach (var prefixFolder in prefixFolders)
        {
            fullPrefixPath = Path.Combine(fullPrefixPath, prefixFolder);
            TestFileSystem.Directory.CreateDirectory(fullPrefixPath);
        }

        foreach (var file in filesMockData)
        {
            using var fileStream = TestFileSystem.File.Create(Path.Join(fullPrefixPath, file));
            fileStream.Write(fileContent);
        }

        var fileSystemSource = new FileSystemStorage(
            new FilesInFileSystemConfig { Path = fullPrefixPath }, TestFileSystem, Formatting.None)
        {
            _context = Globals.Context
        };

        var retrieveSerializedMethod = fileSystemSource.GetType()
            .GetMethod("RetrieveSerialized", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // Act
        var output = ((IEnumerable<byte[]>)retrieveSerializedMethod.Invoke(fileSystemSource, [null])).ToList();

        // Assert
        CollectionAssert.AreEqual(expectedOutput, output);
    }
}