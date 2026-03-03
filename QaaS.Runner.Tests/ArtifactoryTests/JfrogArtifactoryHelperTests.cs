using System.Net;
using System.Text;
using System.Text.Json;
using Moq;
using NUnit.Framework;
using QaaS.Runner.Artifactory;

namespace QaaS.Runner.Tests.ArtifactoryTests;

public class JfrogArtifactoryHelperTests
{
    [Test,
     TestCase("REDA",
         "REDA"),
     TestCase("REDA",
         "REDA")]
    public void
        TestParseArtifactoryFolderUrlToStorageApiUrl_CallFunctionWithValidArtifactoryUrl_ShouldReturnExpectedOutput
        (string folderUrl, string expectedStorageApiUrl)
    {
        // Act
        var storageApiUrl = JfrogArtifactoryHelper.ParseArtifactoryFolderUrlToStorageApiUrl(folderUrl);

        // Assert
        Assert.That(storageApiUrl, Is.EqualTo(expectedStorageApiUrl));
    }

    [Test,
     TestCase("REDA"),
     TestCase("REDA"),
     TestCase("")]
    public void TestParseArtifactoryFolderUrlToStorageApiUrl_CallFunctionWithInvalidUrl_ShouldThrowUriFormatException
        (string invalidFolderUrl)
    {
        // Act + Assert
        Assert.Throws<UriFormatException>(() =>
            JfrogArtifactoryHelper.ParseArtifactoryFolderUrlToStorageApiUrl(invalidFolderUrl));
    }

    [Test]
    public void
        TestParseArtifactoryFolderUrlToStorageApiUrl_CallFunctionWithInvalidArtifactoryUrl_ShouldThrowArgumentException
        ()
    {
        // Act + Assert
        Assert.Throws<ArgumentException>(() => JfrogArtifactoryHelper.ParseArtifactoryFolderUrlToStorageApiUrl
            ("REDA"));
    }

    [Test]
    public void TestGetUrlsToAllFilesInArtifactoryFolder_WithNestedStructure_ReturnsAllFilePaths()
    {
        // Arrange
        var mockHttpClient = new Mock<HttpClient>();
        var baseUrl = "https://test-artifactory.com/artifactory/folder1/folder2";
        var storageApiUrl = "https://test-artifactory.com/artifactory/api/storage/folder1/folder2";

        // Mock response for the storage API call
        var responseContent = new ArtifactoryApiStorageResponse
        {
            Children = new List<ArtifactoryChild>
            {
                new ArtifactoryChild { Uri = "file1.txt" },
                new ArtifactoryChild { Uri = "subfolder/" },
                new ArtifactoryChild { Uri = "anotherfile.zip" }
            }
        };

        var json = JsonSerializer.Serialize(responseContent);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        mockHttpClient.Setup(http => http.GetAsync(storageApiUrl)).ReturnsAsync(httpResponse);

        // Mock the recursive call for subfolder - need to include the full path
        var subfolderResponseContent = new ArtifactoryApiStorageResponse
        {
            Children = new List<ArtifactoryChild>
            {
                new ArtifactoryChild { Uri = "nestedfile.txt" }
            }
        };
        var subpathStorageApiUrls =
            responseContent.Children
                .Select(artifactoryItem =>
                    Path.Combine(storageApiUrl, artifactoryItem.Uri!).Replace("\\", "/"))
                .Append("https://test-artifactory.com/artifactory/api/storage/folder1/folder2/subfolder/nestedfile.txt")
                .ToArray();

        var subfolderJson = JsonSerializer.Serialize(subfolderResponseContent);
        var subfolderHttpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(subfolderJson, Encoding.UTF8, "application/json")
        };

        var subfileResponseContent = new ArtifactoryApiStorageResponse { };
        var subfileJson = JsonSerializer.Serialize(subfileResponseContent);
        var subfileHttpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(subfileJson, Encoding.UTF8, "application/json")
        };

        mockHttpClient.Setup(http => http.GetAsync(It.IsIn(subpathStorageApiUrls)))
            .ReturnsAsync((string s) => Path.HasExtension(s) ? subfileHttpResponse : subfolderHttpResponse);

        var helper = new JfrogArtifactoryHelper();

        // Act
        var result = helper.GetUrlsToAllFilesInArtifactoryFolder(baseUrl, mockHttpClient.Object).ToList();

        // Assert
        Assert.That(result.Count, Is.EqualTo(3));
        Assert.That(result, Contains.Item("https://test-artifactory.com/artifactory/folder1/folder2/file1.txt"));
        Assert.That(result, Contains.Item("https://test-artifactory.com/artifactory/folder1/folder2/anotherfile.zip"));
        Assert.That(result,
            Contains.Item("https://test-artifactory.com/artifactory/folder1/folder2/subfolder/nestedfile.txt"));
    }

    [Test,
     TestCase("https://test-artifactory.com/artifactory/singlefile.txt", 1),
     TestCase("https://test-artifactory.com/artifactory/folder", 1)]
    public void TestGetUrlsToAllFilesInArtifactoryFolder_WithSingleFileOrEmptyChildren_ReturnsSinglePath(
        string folderUrl, int expectedCount)
    {
        // Arrange
        var mockHttpClient = new Mock<HttpClient>();
        var storageApiUrl = "https://test-artifactory.com/artifactory/api/storage/" +
            folderUrl.Split('/').LastOrDefault() ?? "";

        // Mock response for single file or empty children
        // When Children is null, it represents a file, not a directory
        var responseContent = new ArtifactoryApiStorageResponse
        {
            Children = null // No children means it's a file
        };

        var json = JsonSerializer.Serialize(responseContent);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        mockHttpClient.Setup(http => http.GetAsync(storageApiUrl))
            .ReturnsAsync(httpResponse);

        var helper = new JfrogArtifactoryHelper();

        // Act
        var result = helper.GetUrlsToAllFilesInArtifactoryFolder(folderUrl, mockHttpClient.Object).ToList();

        // Assert
        Assert.That(result.Count, Is.EqualTo(expectedCount));
        Assert.That(result[0], Is.EqualTo(folderUrl));
    }

    [Test,
     TestCase(HttpStatusCode.NotFound, typeof(HttpRequestException)),
     TestCase(HttpStatusCode.InternalServerError, typeof(HttpRequestException))]
    public void TestGetUrlsToAllFilesInArtifactoryFolder_WhenHttpCallFails_ThrowsException(
        HttpStatusCode statusCode, Type expectedExceptionType)
    {
        // Arrange
        var mockHttpClient = new Mock<HttpClient>();
        var folderUrl = "https://test-artifactory.com/artifactory/folder";
        var storageApiUrl = "https://test-artifactory.com/artifactory/api/storage/folder";

        // Mock failed HTTP response
        var httpResponse = new HttpResponseMessage(statusCode);

        mockHttpClient.Setup(http => http.GetAsync(storageApiUrl))
            .ReturnsAsync(httpResponse);

        var helper = new JfrogArtifactoryHelper();

        // Act & Assert
        Assert.Throws(expectedExceptionType, () =>
            helper.GetUrlsToAllFilesInArtifactoryFolder(folderUrl, mockHttpClient.Object).ToList());
    }

    [Test]
    public void TestGetUrlsToAllFilesInArtifactoryFolder_WhenChildHasNoUri_ThrowsArgumentException()
    {
        // Arrange
        var mockHttpClient = new Mock<HttpClient>();
        var folderUrl = "https://test-artifactory.com/artifactory/folder";
        var storageApiUrl = "https://test-artifactory.com/artifactory/api/storage/folder";

        // Mock response with child that has no URI
        var responseContent = new ArtifactoryApiStorageResponse
        {
            Children = new List<ArtifactoryChild>
            {
                new ArtifactoryChild { Uri = null } // Missing URI
            }
        };

        var json = JsonSerializer.Serialize(responseContent);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        mockHttpClient.Setup(http => http.GetAsync(storageApiUrl))
            .ReturnsAsync(httpResponse);

        var helper = new JfrogArtifactoryHelper();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            helper.GetUrlsToAllFilesInArtifactoryFolder(folderUrl, mockHttpClient.Object).ToList());
    }
}

// Add these classes to support the tests (they would typically be in the main project)
public class ArtifactoryApiStorageResponse
{
    public List<ArtifactoryChild>? Children { get; set; }
}

public class ArtifactoryChild
{
    public string? Uri { get; set; }
}