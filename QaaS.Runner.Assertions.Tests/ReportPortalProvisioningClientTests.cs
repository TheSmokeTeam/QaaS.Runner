using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using QaaS.Runner.Assertions;
using QaaS.Runner.Assertions.ConfigurationObjects;

namespace QaaS.Runner.Assertions.Tests;

[TestFixture]
public class ReportPortalProvisioningClientTests
{
    [Test]
    public async Task EnsureProjectAccessAsync_WithManagedBot_CreatesProjectUserApiKeyAndFilters()
    {
        var requests = new List<CapturedRequest>();
        using var httpClient = BuildHttpClient(request =>
        {
            var capturedRequest = CaptureRequest(request);
            requests.Add(capturedRequest);

            return (request.Method.Method, request.RequestUri!.AbsoluteUri) switch
            {
                ("POST", "http://localhost:8080/uat/sso/oauth/token")
                    when capturedRequest.Body.Contains("username=superadmin", StringComparison.Ordinal)
                    => BuildJsonResponse(HttpStatusCode.OK, """{"access_token":"bootstrap-token"}"""),
                ("GET", "http://localhost:8080/api/v1/project/names/search?term=Smoke")
                    => BuildJsonResponse(HttpStatusCode.OK, "[]"),
                ("POST", "http://localhost:8080/api/v1/project")
                    => BuildJsonResponse(HttpStatusCode.Created, """{"id":4}"""),
                ("GET", "http://localhost:8080/api/users/qaas-rp-smoke")
                    => new HttpResponseMessage(HttpStatusCode.NotFound),
                ("POST", "http://localhost:8080/api/users")
                    => BuildJsonResponse(HttpStatusCode.Created, """{"id":10}"""),
                ("POST", "http://localhost:8080/uat/sso/oauth/token")
                    when capturedRequest.Body.Contains("username=qaas-rp-smoke", StringComparison.Ordinal)
                    => BuildJsonResponse(HttpStatusCode.OK, """{"access_token":"bot-token"}"""),
                ("GET", "http://localhost:8080/api/users/10/api-keys")
                    => BuildJsonResponse(HttpStatusCode.OK, """{"items":[]}"""),
                ("POST", "http://localhost:8080/api/users/10/api-keys")
                    => BuildJsonResponse(HttpStatusCode.Created,
                        """{"id":20,"name":"qaas-runner","api_key":"managed-api-key"}"""),
                ("GET", "http://localhost:8080/api/v1/Smoke/filter?page.page=0&page.size=200")
                    => BuildJsonResponse(HttpStatusCode.OK,
                        """{"content":[],"page":{"number":1,"size":200,"totalElements":0,"totalPages":0}}"""),
                ("POST", "http://localhost:8080/api/v1/Smoke/filter")
                    => BuildJsonResponse(HttpStatusCode.Created, """{"id":1}"""),
                _ => throw new InvalidOperationException(
                    $"Unexpected request {request.Method} {request.RequestUri} with body `{capturedRequest.Body}`")
            };
        });

        using var client = new ReportPortalProvisioningClient(httpClient);
        var result = await client.EnsureProjectAccessAsync(CreateManagedSettings(), Globals.Logger);

        Assert.That(result.Project, Is.EqualTo("Smoke"));
        Assert.That(result.ApiKey, Is.EqualTo("managed-api-key"));
        Assert.That(requests.Count(request => request.Method == "POST" &&
                                              request.Uri == "http://localhost:8080/api/v1/Smoke/filter"),
            Is.EqualTo(4));

        var createUserRequest = requests.Single(request => request.Method == "POST" &&
                                                           request.Uri == "http://localhost:8080/api/users");
        Assert.That(createUserRequest.Body, Does.Contain(@"""login"":""qaas-rp-smoke"""));
        Assert.That(createUserRequest.Body, Does.Contain(@"""externalId"":""qaas-reportportal/Smoke"""));
        Assert.That(createUserRequest.Body, Does.Contain(@"""projectRole"":""PROJECT_MANAGER"""));
        Assert.That(createUserRequest.Body, Does.Contain(@"""defaultProject"":""Smoke"""));
    }

    [Test]
    public async Task EnsureProjectAccessAsync_WithExistingManagedBot_ReusesApiKey()
    {
        var requests = new List<CapturedRequest>();
        using var httpClient = BuildHttpClient(request =>
        {
            var capturedRequest = CaptureRequest(request);
            requests.Add(capturedRequest);

            return (request.Method.Method, request.RequestUri!.AbsoluteUri) switch
            {
                ("POST", "http://localhost:8080/uat/sso/oauth/token")
                    when capturedRequest.Body.Contains("username=superadmin", StringComparison.Ordinal)
                    => BuildJsonResponse(HttpStatusCode.OK, """{"access_token":"bootstrap-token"}"""),
                ("GET", "http://localhost:8080/api/v1/project/names/search?term=Smoke")
                    => BuildJsonResponse(HttpStatusCode.OK, """["Smoke"]"""),
                ("GET", "http://localhost:8080/api/users/qaas-rp-smoke")
                    => BuildJsonResponse(HttpStatusCode.OK,
                        """{"id":10,"userId":"qaas-rp-smoke","externalId":"qaas-reportportal/Smoke"}"""),
                ("GET", "http://localhost:8080/api/users/qaas-rp-smoke/projects")
                    => BuildJsonResponse(HttpStatusCode.OK,
                        """{"Smoke":{"projectRole":"PROJECT_MANAGER","entryType":"INTERNAL","projectId":4}}"""),
                ("POST", "http://localhost:8080/uat/sso/oauth/token")
                    when capturedRequest.Body.Contains("username=qaas-rp-smoke", StringComparison.Ordinal)
                    => BuildJsonResponse(HttpStatusCode.OK, """{"access_token":"bot-token"}"""),
                ("GET", "http://localhost:8080/api/users/10/api-keys")
                    => BuildJsonResponse(HttpStatusCode.OK,
                        """{"items":[{"id":20,"name":"qaas-runner","api_key":"reused-api-key"}]}"""),
                ("GET", "http://localhost:8080/api/v1/Smoke/filter?page.page=0&page.size=200")
                    => BuildJsonResponse(HttpStatusCode.OK,
                        """{"content":[],"page":{"number":1,"size":200,"totalElements":0,"totalPages":0}}"""),
                ("POST", "http://localhost:8080/api/v1/Smoke/filter")
                    => BuildJsonResponse(HttpStatusCode.Created, """{"id":1}"""),
                _ => throw new InvalidOperationException(
                    $"Unexpected request {request.Method} {request.RequestUri} with body `{capturedRequest.Body}`")
            };
        });

        using var client = new ReportPortalProvisioningClient(httpClient);
        var result = await client.EnsureProjectAccessAsync(CreateManagedSettings(), Globals.Logger);

        Assert.That(result.ApiKey, Is.EqualTo("reused-api-key"));
        Assert.That(requests.Any(request =>
            request.Method == "POST" && request.Uri == "http://localhost:8080/api/users/10/api-keys"), Is.False);
    }

    [Test]
    public void EnsureProjectAccessAsync_WithConflictingManagedBot_ThrowsInvalidOperationException()
    {
        using var httpClient = BuildHttpClient(request =>
        {
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            return (request.Method.Method, request.RequestUri!.AbsoluteUri) switch
            {
                ("POST", "http://localhost:8080/uat/sso/oauth/token")
                    when body.Contains("username=superadmin", StringComparison.Ordinal)
                    => BuildJsonResponse(HttpStatusCode.OK, """{"access_token":"bootstrap-token"}"""),
                ("GET", "http://localhost:8080/api/v1/project/names/search?term=Smoke")
                    => BuildJsonResponse(HttpStatusCode.OK, """["Smoke"]"""),
                ("GET", "http://localhost:8080/api/users/qaas-rp-smoke")
                    => BuildJsonResponse(HttpStatusCode.OK,
                        """{"id":10,"userId":"qaas-rp-smoke","externalId":"someone-else"}"""),
                _ => throw new InvalidOperationException($"Unexpected request {request.Method} {request.RequestUri}")
            };
        });

        using var client = new ReportPortalProvisioningClient(httpClient);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.EnsureProjectAccessAsync(CreateManagedSettings(), Globals.Logger));

        Assert.That(exception!.Message, Does.Contain("is not managed by QaaS"));
    }

    private static ReportPortalSettings CreateManagedSettings()
    {
        return ReportPortalConfig.Resolve(new ReportPortalConfig
        {
            Endpoint = "http://localhost:8080"
        }, new ReportPortalRunDescriptor(
            "Smoke",
            "QaaS",
            ["Session A", "Session B"],
            "run",
            new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero)));
    }

    private static CapturedRequest CaptureRequest(HttpRequestMessage request)
    {
        return new CapturedRequest(
            request.Method.Method,
            request.RequestUri!.AbsoluteUri,
            request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty);
    }

    private static HttpResponseMessage BuildJsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpClient BuildHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) => responseFactory(request));

        return new HttpClient(handler.Object);
    }

    private sealed record CapturedRequest(string Method, string Uri, string Body);
}
