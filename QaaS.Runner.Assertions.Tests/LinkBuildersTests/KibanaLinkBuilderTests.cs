using System;
using System.Collections.Generic;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using QaaS.Runner.Assertions.ConfigurationObjects.LinkConfigs;
using QaaS.Runner.Assertions.LinkBuilders;

namespace QaaS.Runner.Assertions.Tests.LinkBuildersTests;

public class KibanaLinkBuilderTests
{
    [Test]
    public void TestBuildWithAllAdditions_BuildUrlWithAllPossibleOptions_UrlShouldContainStringsFromAllOptions()
    {
        const string url = "url",
            dataViewId = "b337f9fe-6f76-4240-b47b-a149a5d154f3",
            timeStampField = "@timestamp",
            kqlQuery = "severity.keyword : \"INFO\"",
            linkName = "test";

        // Arrange
        var builder = new KibanaLink(linkName, new KibanaLinkConfig
        {
            Url = url,
            TimestampField = timeStampField,
            DataViewId = dataViewId,
            KqlQuery = kqlQuery
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
        StringAssert.Contains(Uri.EscapeDataString(dataViewId), fullUrl.Value);
        StringAssert.Contains(Uri.EscapeDataString(timeStampField), fullUrl.Value);
        StringAssert.Contains(Uri.EscapeDataString(kqlQuery), fullUrl.Value);
        Assert.AreEqual(linkName, fullUrl.Key);
    }
}