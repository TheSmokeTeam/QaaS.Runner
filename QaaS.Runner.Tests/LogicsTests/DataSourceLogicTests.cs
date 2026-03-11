using Moq;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Runner.Logics;

namespace QaaS.Runner.Tests.LogicsTests;

public class DataSourceLogicTests
{
    [Test]
    public void TestRun_WithDataSources_ReturnsExecutionDataWithAddedDataSources()
    {
        // Arrange
        var mockDataSource1 = new Mock<DataSource>();
        var mockDataSource2 = new Mock<DataSource>();
        var mockDataSources = new List<DataSource> { mockDataSource1.Object, mockDataSource2.Object };
        var context = new InternalContext { Logger = Globals.Logger };
        var dataSourceLogic = new DataSourceLogic(mockDataSources, context);
        var executionData = new ExecutionData();

        // Act
        var result = dataSourceLogic.Run(executionData);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.SameAs(executionData));
        Assert.That(executionData.DataSources, Has.Count.EqualTo(2));
        Assert.That(executionData.DataSources, Is.All.AnyOf(mockDataSource1.Object, mockDataSource2.Object));
    }
}
