using System.Collections.Generic;
using QaaS.Framework.SDK.ContextObjects;
using NUnit.Framework;

namespace QaaS.Runner.Infrastructure.Tests;

[TestFixture]
public class ContextArtifactExtensionsTests
{
    [Test]
    public void RenderedTemplates_WithSharedGlobalDictionary_AreScopedPerExecutionContext()
    {
        var sharedGlobalDict = new Dictionary<string, object?>();
        var firstContext = CreateContext(sharedGlobalDict, "exec-a", "case-a");
        var secondContext = CreateContext(sharedGlobalDict, "exec-b", "case-b");

        firstContext.SetRenderedConfigurationTemplate("template-a");
        secondContext.SetRenderedConfigurationTemplate("template-b");

        Assert.That(firstContext.GetRenderedConfigurationTemplate(), Is.EqualTo("template-a"));
        Assert.That(secondContext.GetRenderedConfigurationTemplate(), Is.EqualTo("template-b"));
    }

    [Test]
    public void SessionLogs_WithSharedGlobalDictionary_AreScopedPerExecutionContext()
    {
        var sharedGlobalDict = new Dictionary<string, object?>();
        var firstContext = CreateContext(sharedGlobalDict, "exec-a", "case-a");
        var secondContext = CreateContext(sharedGlobalDict, "exec-b", "case-b");

        firstContext.AppendSessionLog("shared-session", "first log line");
        secondContext.AppendSessionLog("shared-session", "second log line");

        var firstLog = firstContext.GetSessionLog("shared-session");
        var secondLog = secondContext.GetSessionLog("shared-session");

        Assert.That(firstLog, Does.Contain("first log line"));
        Assert.That(firstLog, Does.Not.Contain("second log line"));
        Assert.That(secondLog, Does.Contain("second log line"));
        Assert.That(secondLog, Does.Not.Contain("first log line"));
    }

    private static InternalContext CreateContext(Dictionary<string, object?> sharedGlobalDict, string executionId,
        string caseName)
    {
        return new InternalContext
        {
            Logger = Globals.Logger,
            ExecutionId = executionId,
            CaseName = caseName,
            InternalGlobalDict = sharedGlobalDict
        };
    }
}
