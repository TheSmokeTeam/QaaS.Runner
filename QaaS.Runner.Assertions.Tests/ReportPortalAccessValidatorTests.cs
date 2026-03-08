using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using QaaS.Runner.Assertions.ConfigurationObjects;

namespace QaaS.Runner.Assertions.Tests;

[TestFixture]
public class ReportPortalAccessValidatorTests
{
    [Test]
    public async Task EnsureWriteAccessAsync_WithMissingTeam_ReturnsFailureWithoutHttpCall()
    {
        using var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        using var validator = new ReportPortalAccessValidator(httpClient);

        var result = await validator.EnsureWriteAccessAsync(CreateSettings(team: null), Globals.Logger);

        Assert.That(result.CanPublish, Is.False);
        Assert.That(result.FailureReason, Does.Contain("MetaData.Team"));
        Assert.That(handler.RequestCount, Is.Zero);
    }

    [Test]
    public async Task EnsureWriteAccessAsync_WithMissingApiKey_ReturnsFailureWithoutHttpCall()
    {
        using var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        using var validator = new ReportPortalAccessValidator(httpClient);

        var result = await validator.EnsureWriteAccessAsync(CreateSettings(apiKey: null), Globals.Logger);

        Assert.That(result.CanPublish, Is.False);
        Assert.That(result.FailureReason, Does.Contain(ReportPortalConfig.ApiKeyEnvironmentVariable));
        Assert.That(handler.RequestCount, Is.Zero);
    }

    [Test]
    public async Task EnsureWriteAccessAsync_WithUnauthorizedApiKey_ReturnsFailure()
    {
        using var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("unauthorized", Encoding.UTF8, "text/plain")
            });
        using var httpClient = new HttpClient(handler);
        using var validator = new ReportPortalAccessValidator(httpClient);

        var result = await validator.EnsureWriteAccessAsync(CreateSettings(), Globals.Logger);

        Assert.That(result.CanPublish, Is.False);
        Assert.That(result.FailureReason, Does.Contain("API key"));
        Assert.That(handler.RequestCount, Is.EqualTo(1));
    }

    [Test]
    public async Task EnsureWriteAccessAsync_WithMissingProject_ReturnsFailure()
    {
        using var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"message\":\"not found\"}", Encoding.UTF8,
                    "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var validator = new ReportPortalAccessValidator(httpClient);

        var result = await validator.EnsureWriteAccessAsync(CreateSettings(team: "Smoke"), Globals.Logger);

        Assert.That(result.CanPublish, Is.False);
        Assert.That(result.FailureReason, Does.Contain("no accessible project matches team `Smoke`"));
        Assert.That(handler.RequestCount, Is.EqualTo(1));
    }

    [Test]
    public async Task EnsureWriteAccessAsync_MatchesProjectCaseInsensitively()
    {
        using var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"projectName\":\"SMOKE\"}", Encoding.UTF8,
                    "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var validator = new ReportPortalAccessValidator(httpClient);

        var result = await validator.EnsureWriteAccessAsync(CreateSettings(team: "smoke"), Globals.Logger);

        Assert.That(result.CanPublish, Is.True);
        Assert.That(result.Project, Is.EqualTo("SMOKE"));
        Assert.That(result.EndpointUri!.AbsoluteUri, Is.EqualTo("http://localhost:8080/api/"));
    }

    [Test]
    public async Task EnsureWriteAccessAsync_CachesSuccessfulValidationPerProject()
    {
        using var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"projectName\":\"Smoke\"}", Encoding.UTF8,
                    "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var validator = new ReportPortalAccessValidator(httpClient);
        var settings = CreateSettings();

        var firstResult = await validator.EnsureWriteAccessAsync(settings, Globals.Logger);
        var secondResult = await validator.EnsureWriteAccessAsync(settings, Globals.Logger);

        Assert.That(firstResult.CanPublish, Is.True);
        Assert.That(secondResult.CanPublish, Is.True);
        Assert.That(handler.RequestCount, Is.EqualTo(1));
    }

    private static ReportPortalSettings CreateSettings(string? team = "Smoke", string? apiKey = "api-key",
        string? endpoint = "http://localhost:8080")
    {
        return new ReportPortalSettings(
            true,
            endpoint,
            apiKey,
            team,
            "QaaS",
            ["session-a"],
            "launch",
            "description",
            false,
            new Dictionary<string, string>(),
            null);
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responseFactory(request));
        }
    }
}
