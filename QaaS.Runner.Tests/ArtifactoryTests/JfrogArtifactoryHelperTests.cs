using System.Net;
using System.Text;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using QaaS.Runner.Artifactory;

namespace QaaS.Runner.Tests.ArtifactoryTests;

public class JfrogArtifactoryHelperTests
{
    [Test,
     TestCase("https://test-artifactory.com/artifactory/repository/path",
         "https://test-artifactory.com/artifactory/api/storage/repository/path"),
     TestCase("http://localhost:8081/artifactory/libs-release-local/com/acme",
         "http://localhost:8081/artifactory/api/storage/libs-release-local/com/acme")]
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
     TestCase("not-a-valid-uri"),
     TestCase("/relative/path"),
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
            ("https://test-artifactory.com/repository/path"));
    }

    [Test]
    public void TestGetUrlsToAllFilesInArtifactoryFolder_WithNestedStructure_ReturnsAllFilePaths()
    {
        // Arrange
        var helper = new JfrogArtifactoryHelper();
        var baseUrl = "https://test-artifactory.com/artifactory/folder1/folder2";
        var storageApiUrl = JfrogArtifactoryHelper.ParseArtifactoryFolderUrlToStorageApiUrl(baseUrl);
        var fileStorageApiUrl = Path.Join(storageApiUrl, "file1.txt").Replace('\\', '/');
        var subfolderStorageApiUrl = Path.Join(storageApiUrl, "subfolder/").Replace('\\', '/');
        var zipStorageApiUrl = Path.Join(storageApiUrl, "anotherfile.zip").Replace('\\', '/');
        var nestedFileStorageApiUrl = Path.Join(subfolderStorageApiUrl, "nestedfile.txt").Replace('\\', '/');

        var responses = new Dictionary<string, HttpResponseMessage>
        {
            [storageApiUrl] = BuildOkJsonResponse(
                """{"children":[{"uri":"file1.txt"},{"uri":"subfolder/"},{"uri":"anotherfile.zip"}]}"""),
            [fileStorageApiUrl] = BuildOkJsonResponse("""{"children":null}"""),
            [subfolderStorageApiUrl] = BuildOkJsonResponse("""{"children":[{"uri":"nestedfile.txt"}]}"""),
            [zipStorageApiUrl] = BuildOkJsonResponse("""{"children":null}"""),
            [nestedFileStorageApiUrl] = BuildOkJsonResponse("""{"children":null}""")
        };

        using var httpClient = BuildHttpClient(uri =>
        {
            if (responses.TryGetValue(uri.ToString(), out var response))
                return response;

            throw new InvalidOperationException($"Missing mocked response for uri {uri}");
        });

        // Act
        var result = helper.GetUrlsToAllFilesInArtifactoryFolder(baseUrl, httpClient).ToList();

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
        var storageApiUrl = JfrogArtifactoryHelper.ParseArtifactoryFolderUrlToStorageApiUrl(folderUrl);
        using var httpClient = BuildHttpClient(uri =>
            uri.ToString() == storageApiUrl
                ? BuildOkJsonResponse("""{"children":null}""")
                : throw new InvalidOperationException($"Missing mocked response for uri {uri}"));

        var helper = new JfrogArtifactoryHelper();

        // Act
        var result = helper.GetUrlsToAllFilesInArtifactoryFolder(folderUrl, httpClient).ToList();

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
        var folderUrl = "https://test-artifactory.com/artifactory/folder";
        var storageApiUrl = "https://test-artifactory.com/artifactory/api/storage/folder";
        using var httpClient = BuildHttpClient(uri =>
            uri.ToString() == storageApiUrl
                ? new HttpResponseMessage(statusCode)
                : throw new InvalidOperationException($"Missing mocked response for uri {uri}"));

        var helper = new JfrogArtifactoryHelper();

        // Act & Assert
        Assert.Throws(expectedExceptionType, () =>
            helper.GetUrlsToAllFilesInArtifactoryFolder(folderUrl, httpClient).ToList());
    }

    [Test]
    public void TestGetUrlsToAllFilesInArtifactoryFolder_WhenChildHasNoUri_ThrowsArgumentException()
    {
        // Arrange
        var folderUrl = "https://test-artifactory.com/artifactory/folder";
        var storageApiUrl = "https://test-artifactory.com/artifactory/api/storage/folder";
        using var httpClient = BuildHttpClient(uri =>
            uri.ToString() == storageApiUrl
                ? BuildOkJsonResponse("""{"children":[{}]}""")
                : throw new InvalidOperationException($"Missing mocked response for uri {uri}"));

        var helper = new JfrogArtifactoryHelper();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            helper.GetUrlsToAllFilesInArtifactoryFolder(folderUrl, httpClient).ToList());
    }

    private static HttpResponseMessage BuildOkJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpClient BuildHttpClient(Func<Uri, HttpResponseMessage> responseFactory)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) => responseFactory(request.RequestUri!));

        return new HttpClient(handler.Object);
    }
}
