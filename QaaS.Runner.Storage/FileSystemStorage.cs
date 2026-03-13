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
        var directoryFullPath = GetDirectoryFullPath(caseName);
        _context.Logger.LogInformation(
            "Storing {SessionCount} session data item(s) in directory {DirectoryPath}",
            sessionFileNameAndSerializedSessionDataItemsToStorePair.Count, directoryFullPath);

        foreach (var fileNameSerializedSessionDataPair in sessionFileNameAndSerializedSessionDataItemsToStorePair)
        {
            var sessionDataFilePath =
                Infrastructure.FileSystemExtensions.CombineUnderRoot(directoryFullPath,
                    fileNameSerializedSessionDataPair.Key);
            var sessionDataDirectoryPath = Path.GetDirectoryName(sessionDataFilePath) ?? directoryFullPath;
            // Check if the directory needed to write the session data to exists, if not create it!
            if (!_fileSystem.Directory.Exists(sessionDataDirectoryPath))
                _fileSystem.Directory.CreateDirectory(sessionDataDirectoryPath);

            _context.Logger.LogDebug("Writing session data file {SessionDataFilePath}", sessionDataFilePath);
            _fileSystem.File.WriteAllBytes(sessionDataFilePath, fileNameSerializedSessionDataPair.Value);
        }
    }

    protected override IEnumerable<byte[]> RetrieveSerialized(string? caseName)
    {
        var directoryFullPath = GetDirectoryFullPath(caseName);
        if (!_fileSystem.Directory.Exists(directoryFullPath))
        {
            _context.Logger.LogWarning("Storage directory {DirectoryPath} was not found during retrieval. Returning no session data.",
                directoryFullPath);
            return [];
        }

        var files = _fileSystem.Directory.GetFiles(directoryFullPath,
            _configuration.SearchPattern, SearchOption.AllDirectories);
        _context.Logger.LogInformation("Found {FileCount} file(s) to retrieve from {DirectoryPath}",
            files.Length, directoryFullPath);
        return files.Select(file => _fileSystem.File.ReadAllBytes(file));
    }

    private string GetDirectoryFullPath(string? caseName)
    {
        var configuredDirectory = CaseStorageHandler.HandleCaseWithFileSystem(_configuration, caseName);
        return Path.GetFullPath(Path.IsPathRooted(configuredDirectory)
            ? configuredDirectory
            : Path.Combine(Environment.CurrentDirectory, configuredDirectory));
    }
}
