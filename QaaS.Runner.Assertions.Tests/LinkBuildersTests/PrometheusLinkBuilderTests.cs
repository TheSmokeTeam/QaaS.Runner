using System;
using System.Collections.Generic;
using NUnit.Framework;
using QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;
using QaaS.Runner.Assertions.LinkBuilders;

namespace QaaS.Runner.Assertions.Tests.LinkBuildersTests;

public class PrometheusLinkBuilderTests
{
    [Test]
    public void TestBuildWithAllAdditions_BuildUrlWithAllPossibleOptions_UrlShouldContainStringsFromAllOptions()
    {
        const string url = "REDA",
            linkName = "test",
            metricA = "input",
            metricB = "output";

        // Arrange
        var builder = new PrometheusLink(linkName, new PrometheusLinkConfig
        {
            Url = url,
            Expressions = new[] { metricA, metricB }
        });
        var startTimeOne = new DateTime(2023, 11, 19, 1, 1, 1);
        var endTimeOne = new DateTime(2023, 11, 19, 6, 6, 6);

        var startTimeTwo = new DateTime(2023, 11, 20, 1, 1, 1);
        var endTimeTwo = new DateTime(2023, 11, 20, 6, 6, 6);

        // Act
        var fullUrl = builder
            .GetLink(new List<KeyValuePair<DateTime, DateTime>>
            {
                new(startTimeOne, endTimeOne),
                new(startTimeTwo, endTimeTwo)
            });

        // Assert
        StringAssert.Contains(url, fullUrl.Value);
        StringAssert.Contains(metricA, fullUrl.Value);
        StringAssert.Contains(metricB, fullUrl.Value);
        Assert.AreEqual(linkName, fullUrl.Key);
    }
}