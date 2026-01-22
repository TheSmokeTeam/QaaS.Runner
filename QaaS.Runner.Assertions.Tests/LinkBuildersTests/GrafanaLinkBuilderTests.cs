using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;
using QaaS.Runner.Assertions.LinkBuilders;

namespace QaaS.Runner.Assertions.Tests.LinkBuildersTests;

public class GrafanaLinkBuilderTests
{
    [Test]
    public void TestBuildWithAllAdditions_BuildUrlWithAllPossibleOptions_UrlShouldContainStringsFromAllOptions()
    {
        const string url = "REDA",
            dashboardId = "jEhCoNoSk",
            linkName = "moby",
            variableAKey = "var-namespace",
            variableAValue = "qaas-runner-env-poc",
            variableBKey = "var-site",
            variableBValue = "REDA",
            expectedStartTimeMs = "1722927661000",
            expectedEndTimeMs = "1722927844000";

        // Arrange
        var builder = new GrafanaLink(linkName, new GrafanaLinkConfig
        {
            Url = url,
            DashboardId = "jEhCoNoSk",
            Variables = new List<KeyValuePair<string, string>>
            {
                new(variableAKey, variableAValue),
                new(variableBKey, variableBValue)
            }.ToArray()
        });
        var startTimeOne = new DateTime(2024, 8, 6, 7, 1, 1, DateTimeKind.Utc);
        var endTimeOne = new DateTime(2024, 8, 6, 7, 2, 2, DateTimeKind.Utc);

        var startTimeTwo = new DateTime(2024, 8, 6, 7, 3, 3, DateTimeKind.Utc);
        var endTimeTwo = new DateTime(2024, 8, 6, 7, 4, 4, DateTimeKind.Utc);

        Globals.Logger.LogInformation("Start time is {StartTime}, End time is {EndTime}",
            startTimeOne, endTimeTwo);

        // Act
        var fullUrl = builder
            .GetLink(new List<KeyValuePair<DateTime, DateTime>>
            {
                new(startTimeOne, endTimeOne),
                new(startTimeTwo, endTimeTwo)
            });
        Globals.Logger.LogInformation("Grafana Url is {FullUrl}", fullUrl);

        // Assert
        StringAssert.Contains(url, fullUrl.Value);
        StringAssert.Contains(dashboardId, fullUrl.Value);
        StringAssert.Contains(variableAKey, fullUrl.Value);
        StringAssert.Contains(variableBKey, fullUrl.Value);
        StringAssert.Contains(variableAValue, fullUrl.Value);
        StringAssert.Contains(variableBValue, fullUrl.Value);
        StringAssert.Contains(expectedStartTimeMs, fullUrl.Value);
        StringAssert.Contains(expectedEndTimeMs, fullUrl.Value);
        Assert.AreEqual(linkName, fullUrl.Key);
    }
}