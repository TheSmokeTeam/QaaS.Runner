using System.Text.Json;
using ClosedLibsWrappers.interfaces;

namespace QaaS.Runner.Artifactory;

/// <summary>
///     Helper class containing functions for working against the Jfrog artifactory
/// </summary>
public class JfrogArtifactoryHelper : IJfrogArtifactoryHelper
{
    private const string ArtifactorySegment = "artifactory/", ApiSegment = "api/", StorageSegment = "storage/";
    
    /// <summary>
    /// Parses a given jfrog artifactory folder url to its Storage Api version which returns meta data about the folder
    /// when using http get on
    /// </summary>
    /// <param name="url"> The url to the jfrog artifactory folder </param>
    /// <returns> The url to the storage api of the jfrog artifactory folder </returns>
    public static string ParseArtifactoryFolderUrlToStorageApiUrl(string url)
    {
        var uri = new Uri(url);
        var segments = uri.Segments.ToList();
        var indexOfArtifactorySegment = segments.IndexOf(ArtifactorySegment);
        if (indexOfArtifactorySegment == -1)
        {
            throw new ArgumentException(
                $"Given artifactory url does not contain the {ArtifactorySegment} segment");
        }
        segments.Insert(indexOfArtifactorySegment + 1, ApiSegment);
        segments.Insert(indexOfArtifactorySegment + 2, StorageSegment);
        
        return new UriBuilder(uri.Scheme ,uri.Host ,uri.Port, string.Join("", segments)).Uri.ToString();
    }


    /// <inheritdoc />
    public IEnumerable<string> GetUrlsToAllFilesInArtifactoryFolder(string artifactoryFolderUrl,
        IHttpClient httpClient)
    {
        var storageApiUrl = ParseArtifactoryFolderUrlToStorageApiUrl(artifactoryFolderUrl);
        var getResponse = httpClient.GetAsync(storageApiUrl).Result;
        if (!getResponse.IsSuccessStatusCode)
            throw new HttpRequestException($"Http get on {storageApiUrl} returned status {getResponse?.StatusCode}");
        var children = JsonSerializer
            .Deserialize<ArtifactoryApiStorageResponse>(getResponse.Content.ReadAsStringAsync().Result,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Children;

        // If item has no children meaning its a file itself return it and break
        if (children is not { Count: > 0 })
        {
            yield return artifactoryFolderUrl;
            yield break;
        }

        // Go over all children in item and return their children's file paths
        foreach (var child in children)
        {
            var childUri = child.Uri ?? throw new ArgumentException(
                $"Could not find {nameof(ArtifactoryChild.Uri)} in a child of {artifactoryFolderUrl}");
            foreach (var filePath in
                     GetUrlsToAllFilesInArtifactoryFolder(Path.Join(artifactoryFolderUrl, childUri).Replace('\\', '/'),
                         httpClient))
                yield return filePath;
        }
    }
}