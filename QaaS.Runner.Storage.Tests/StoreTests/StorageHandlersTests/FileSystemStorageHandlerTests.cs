using System.Collections.Immutable;
using System.IO.Abstractions;
using Moq;
using NUnit.Framework;
using QaaS.Framework.Configurations.CommonConfigurationObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Runner.Storage.ConfigurationObjects;

namespace QaaS.Runner.Storage.Tests.StoreTests.StorageHandlersTests;

public class FileSystemStorageHandlerTests
{
    [Test]
    [TestCase(true, 0)]
    [TestCase(false, 0)]
    [TestCase(true, 1)]
    [TestCase(true, 1)]
    [TestCase(true, 100)]
    [TestCase(true, 100)]
    public void TestStore_CallFunctionWithMockFileSystem_ShouldCreateSameNumberOfFilesAsNumberOfItemsToStoreGiven(
        bool doesDirectoryExist, int numberOfItemsToStore)
    {
        // Arrange

        // Mock filesystem
        var mockDirectory = new Mock<IDirectory>();
        mockDirectory.Setup(m => m.Exists(It.IsAny<string>())).Returns(doesDirectoryExist);
        mockDirectory.Setup(m => m.CreateDirectory(It.IsAny<string>()));
        var mockFile = new Mock<IFile>();
        mockFile.Setup(m => m.WriteAllBytes(It.IsAny<string>(), It.IsAny<byte[]>()));

        var mockFileSystem = new Mock<IFileSystem>();
        mockFileSystem.Setup(m => m.Directory).Returns(mockDirectory.Object);
        mockFileSystem.Setup(m => m.File).Returns(mockFile.Object);

        // Create items to store
        var itemsToStore = new List<SessionData>();
        for (var itemToStoreIndex = 0; itemToStoreIndex < numberOfItemsToStore; itemToStoreIndex++)
            itemsToStore.Add(new SessionData { Name = $"session-{itemToStoreIndex}" });

        var storageHandler = new FileSystemStorage(new FilesInFileSystemConfig { Path = "somePath" },
            mockFileSystem.Object, Formatting.Indented)
        {
            _context = Globals.Context
        };

        // Act
        storageHandler.Store(itemsToStore.ToImmutableList()!, null);

        // Assert
        if (!doesDirectoryExist)
            mockDirectory.Verify(m => m.CreateDirectory(It.IsAny<string>()),
                Times.Exactly(numberOfItemsToStore));
        mockDirectory.Verify(m => m.Exists(It.IsAny<string>()),
            Times.Exactly(numberOfItemsToStore));
        mockFile.Verify(m => m.WriteAllBytes(It.IsAny<string>(), It.IsAny<byte[]>()),
            Times.Exactly(numberOfItemsToStore));
    }
}
