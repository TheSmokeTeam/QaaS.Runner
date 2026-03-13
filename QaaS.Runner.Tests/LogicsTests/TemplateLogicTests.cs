using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Runner.Infrastructure;
using QaaS.Runner.Logics;

namespace QaaS.Runner.Tests.LogicsTests;

public class TemplateLogicTests
{
    [Test]
    public void TestRun_WithWriterOrNull_WritesCapturedTemplateCorrectly()
    {
        var context = Globals.GetContextWithMetadata();
        context.SetRenderedConfigurationTemplate("Sessions:\n  - Name: RabbitRoundTrip\n");
        var mockWriter = new Mock<TextWriter>();
        var templateLogic = new TemplateLogic(context, mockWriter.Object);
        var executionData = new ExecutionData();

        var result = templateLogic.Run(executionData);

        mockWriter.Verify(textWriter => textWriter.WriteLine("Sessions:\n  - Name: RabbitRoundTrip\n"), Times.Once());
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.SameAs(executionData));
    }

    [Test]
    public void Run_WhenRenderedTemplateWasNotCaptured_FallsBackToRootConfiguration()
    {
        var context = new InternalContext
        {
            Logger = Globals.Logger,
            RootConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MetaData:System"] = "QaaS",
                    ["MetaData:Team"] = "Smoke"
                })
                .Build(),
            InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>())
        };
        var mockWriter = new Mock<TextWriter>();
        var templateLogic = new TemplateLogic(context, mockWriter.Object);

        templateLogic.Run(new ExecutionData());

        mockWriter.Verify(textWriter => textWriter.WriteLine(It.Is<string>(value =>
            value.Contains("MetaData:", StringComparison.Ordinal) &&
            value.Contains("System: QaaS", StringComparison.Ordinal))), Times.Once());
    }
}
