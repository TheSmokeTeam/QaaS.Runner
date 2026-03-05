using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Configurations.CommonConfigurationObjects;
using QaaS.Runner.Storage.ConfigurationObjects;

namespace QaaS.Runner.Storage;

public class FileSystemStorage : BaseStorage
{
    private readonly FilesInFileSystemConfig _configuration;
    private readonly IFileSystem _fileSystem;

    public FileSystemStorage(FilesInFileSystemConfig configuration, IFileSystem fileSystem,
        Formatting jsonStorageFormat) : base(jsonStorageFormat)
    {
        _fileSystem = fileSystem;
        _configuration = configuration;
    }

    protected override void StoreSerialized(
        IList<KeyValuePair<string, byte[]>> sessionFileNameAndSerializedSessionDataItemsToStorePair, string? caseName)
    {
        var directoryFullPath = Path.Combine(Environment.CurrentDirectory,
            CaseStorageHandler.HandleCaseWithFileSystem(_configuration, caseName));
        _context.Logger.LogInformation(
            "Storing {NumberOfSessionDataItemsToStore} session data items at {DirectoryPath}",
            sessionFileNameAndSerializedSessionDataItemsToStorePair.Count, directoryFullPath);

        foreach (var fileNameSerializedSessionDataPair in sessionFileNameAndSerializedSessionDataItemsToStorePair)
        {
            var sessionDataFilePath = Path.Combine(directoryFullPath, fileNameSerializedSessionDataPair.Key);
            var sessionDataDirectoryPath = Path.GetDirectoryName(sessionDataFilePath) ?? directoryFullPath;
            // Check if the directory needed to write the session data to exists, if not create it!
            if (!_fileSystem.Directory.Exists(sessionDataDirectoryPath))
                _fileSystem.Directory.CreateDirectory(sessionDataDirectoryPath);

            _context.Logger.LogDebug("Storing sessionData at file {SessionDataFilePath}", sessionDataFilePath);
            _fileSystem.File.WriteAllBytes(sessionDataFilePath, fileNameSerializedSessionDataPair.Value);
        }
    }

    protected override IEnumerable<byte[]> RetrieveSerialized(string? caseName)
    {
        var files = _fileSystem.Directory.GetFiles(Path.Combine(Environment.CurrentDirectory,
                CaseStorageHandler.HandleCaseWithFileSystem(_configuration, caseName)),
            _configuration.SearchPattern, SearchOption.AllDirectories);
        _context.Logger.LogInformation("Found {FileCount} files to retrieve", files.Length);
        return files.Select(file => _fileSystem.File.ReadAllBytes(file));
    }
}
