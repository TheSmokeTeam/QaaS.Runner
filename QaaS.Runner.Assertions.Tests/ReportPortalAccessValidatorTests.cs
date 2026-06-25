using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using QaaS.Framework.SDK;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Runner.Assertions.ConfigurationObjects.ReporterConfigs;
using QaaS.Runner.Assertions.Reporters.ReportPortal;

namespace QaaS.Runner.Assertions.Tests;

[TestFixture]
public class ReportPortalAccessValidatorTests
{
    [Test]
    public void BuildLaunchPlans_WhenDisabled_ReturnsNoPlans()
    {
        var reporter = CreateReporter(enabled: false);

        var launchPlans = ReportPortalLaunchPlan.Build(
            [reporter],
            new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero),
            requireQueuedResults: false);

        Assert.That(launchPlans, Is.Empty);
    }

    [Test]
    public async Task EnsureWriteAccessAsync_WithMissingTeam_ReturnsFailureWithoutHttpCall()
    {
        using var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        using var validator = new ReportPortalAccessValidator(httpClient);

        var result = await validator.EnsureWriteAccessAsync(CreatePlan(team: null), Globals.Logger);

        Assert.That(result.CanPublish, Is.False);
        Assert.That(result.FailureReason, Does.Contain("ReportPortal.Project or MetaData.Team"));
        Assert.That(handler.RequestCount, Is.Zero);
    }

    [Test]
    public async Task EnsureWriteAccessAsync_WithMissingApiKey_ReturnsFailureWithoutHttpCall()
    {
        using var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        using var validator = new ReportPortalAccessValidator(httpClient);

        var result = await validator.EnsureWriteAccessAsync(CreatePlan(apiKey: null), Globals.Logger);

        Assert.That(result.CanPublish, Is.False);
        Assert.That(result.FailureReason, Does.Contain("ReportPortal.ApiKey"));
        Assert.That(handler.RequestCount, Is.Zero);
    }

    [Test]
    public async Task EnsureWriteAccessAsync_WithMissingEndpoint_ReturnsFailureWithoutHttpCall()
    {
        using var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        using var validator = new ReportPortalAccessValidator(httpClient);

        var result = await validator.EnsureWriteAccessAsync(CreatePlan(endpoint: null), Globals.Logger);

        Assert.That(result.CanPublish, Is.False);
        Assert.That(result.FailureReason, Does.Contain("ReportPortal.Endpoint"));
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

        var result = await validator.EnsureWriteAccessAsync(CreatePlan(), Globals.Logger);

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

        var result = await validator.EnsureWriteAccessAsync(CreatePlan(team: "Smoke"), Globals.Logger);

        Assert.That(result.CanPublish, Is.False);
        Assert.That(result.FailureReason, Does.Contain("no accessible project matches `Smoke`"));
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

        var result = await validator.EnsureWriteAccessAsync(CreatePlan(team: "smoke"), Globals.Logger);

        Assert.That(result.CanPublish, Is.True);
        Assert.That(result.Project, Is.EqualTo("SMOKE"));
        Assert.That(result.EndpointUri!.AbsoluteUri, Is.EqualTo("http://localhost:8080/api/"));
    }

    [Test]
    public async Task EnsureWriteAccessAsync_CachesSuccessfulValidationPerNormalizedLaunchGroup()
    {
        using var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"projectName\":\"Smoke\"}", Encoding.UTF8,
                    "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var validator = new ReportPortalAccessValidator(httpClient);
        var firstLaunchPlan = CreatePlan(endpoint: "http://localhost:8080");
        var equivalentEndpointLaunchPlan = CreatePlan(endpoint: "http://localhost:8080/api/v1/");

        var firstResult = await validator.EnsureWriteAccessAsync(firstLaunchPlan, Globals.Logger);
        var secondResult = await validator.EnsureWriteAccessAsync(equivalentEndpointLaunchPlan, Globals.Logger);

        Assert.That(firstResult.CanPublish, Is.True);
        Assert.That(secondResult.CanPublish, Is.True);
        Assert.That(handler.RequestCount, Is.EqualTo(1));
    }

    [Test]
    public async Task EnsureWriteAccessAsync_WithDifferentSystem_ValidatesSeparateLaunchGroup()
    {
        using var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"projectName\":\"Smoke\"}", Encoding.UTF8,
                    "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var validator = new ReportPortalAccessValidator(httpClient);

        var firstResult = await validator.EnsureWriteAccessAsync(CreatePlan(system: "QaaS"), Globals.Logger);
        var secondResult = await validator.EnsureWriteAccessAsync(CreatePlan(system: "AnotherSystem"), Globals.Logger);

        Assert.That(firstResult.CanPublish, Is.True);
        Assert.That(secondResult.CanPublish, Is.True);
        Assert.That(handler.RequestCount, Is.EqualTo(2));
    }

    private static ReportPortalLaunchPlan CreatePlan(bool enabled = true, string? team = "Smoke",
        string? apiKey = "api-key", string? endpoint = "http://localhost:8080", string system = "QaaS")
    {
        return ReportPortalLaunchPlan.Build(
            [CreateReporter(enabled, team, apiKey, endpoint, system)],
            new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero),
            requireQueuedResults: false).Single();
    }

    private static ReportPortalReporter CreateReporter(bool enabled = true, string? team = "Smoke",
        string? apiKey = "api-key", string? endpoint = "http://localhost:8080", string system = "QaaS")
    {
        ReportPortalConfig.RegisterDefaults(enabled: false);
        var context = new InternalContext
        {
            Logger = Globals.Logger
        };
        if (team is not null)
        {
            context.InsertValueIntoGlobalDictionary(context.GetMetaDataPath(), new MetaDataConfig
            {
                Team = team,
                System = system
            });
        }

        return new ReportPortalReporter
        {
            Config = new ReportPortalConfig
            {
                Enabled = enabled,
                Endpoint = endpoint,
                ApiKey = apiKey,
                LaunchName = "launch",
                Description = "description"
            },
            Context = context
        };
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
