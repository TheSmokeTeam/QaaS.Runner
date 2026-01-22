using ClosedLibsWrappers.interfaces;

namespace QaaS.Runner.Artifactory;

/// <summary>
///     Contains helper functions for interacting with the jfrog artifactory
/// </summary>
public interface IJfrogArtifactoryHelper
{
    /// <summary>
    ///     Gets the paths to all files under the given artifactory folder recursively,
    ///     meaning sub files will also be given
    /// </summary>
    /// <param name="artifactoryFolderUrl"> The url to the jfrog artifactory folder </param>
    /// <param name="httpClient"> The http client to perform get requests on the artifactory with </param>
    /// <returns> An enumerable of the urls to all child files under the given artifactoryFolderUrl </returns>
    public IEnumerable<string> GetUrlsToAllFilesInArtifactoryFolder(string artifactoryFolderUrl,
        IHttpClient httpClient);
}