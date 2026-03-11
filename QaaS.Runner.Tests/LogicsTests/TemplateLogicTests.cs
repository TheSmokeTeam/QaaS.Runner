using Moq;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.ExecutionObjects;
using QaaS.Runner.Logics;

namespace QaaS.Runner.Tests.LogicsTests;

public class TemplateLogicTests
{
    [Test]
    public void TestRun_WithWriterOrNull_WritesTemplateCorrectly()
    {
        // Arrange
        var context = new Context();
        var mockWriter = new Mock<TextWriter>();
        mockWriter.Setup(textWriter => textWriter.WriteLine(It.IsAny<string>())).Verifiable();
        TextWriter? writer = mockWriter.Object;
        var templateLogic = new TemplateLogic(context, writer);
        var executionData = new ExecutionData();

        // Act
        var result = templateLogic.Run(executionData);

        // Assert
        mockWriter.Verify(textWriter => textWriter.WriteLine(It.IsAny<string>()), Times.Once());
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.SameAs(executionData));
    }
}
