using System;
using System.Collections.Concurrent;
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

    [Test]
    public void GetRenderedConfigurationTemplate_FallsBackToLegacyUnscopedValue()
    {
        var context = CreateContext(new Dictionary<string, object?>(), "exec-a", "case-a");
        context.InsertValueIntoGlobalDictionary(["__RunnerArtifacts", "RenderedTemplate"], "legacy-template");

        Assert.That(context.GetRenderedConfigurationTemplate(), Is.EqualTo("legacy-template"));
    }

    [Test]
    public void AppendSessionLog_IgnoresBlankSessionNameAndMessage()
    {
        var context = CreateContext(new Dictionary<string, object?>(), "exec-a", "case-a");

        context.AppendSessionLog("", "line");
        context.AppendSessionLog("session", "");

        Assert.That(context.GetSessionLog("session"), Is.Null);
    }

    [Test]
    public void GetSessionLog_WhenNoLogWasCaptured_ReturnsNull()
    {
        var context = CreateContext(new Dictionary<string, object?>(), "exec-a", "case-a");

        Assert.That(context.GetSessionLog("missing"), Is.Null);
    }

    [Test]
    public void Artifacts_WithoutExecutionOrCaseName_AreScopedPerContextInstance()
    {
        var sharedGlobalDict = new Dictionary<string, object?>();
        var firstContext = CreateContext(sharedGlobalDict, null!, null!);
        var secondContext = CreateContext(sharedGlobalDict, null!, null!);

        firstContext.SetRenderedConfigurationTemplate("first");
        secondContext.SetRenderedConfigurationTemplate("second");

        Assert.Multiple(() =>
        {
            Assert.That(firstContext.GetRenderedConfigurationTemplate(), Is.EqualTo("first"));
            Assert.That(secondContext.GetRenderedConfigurationTemplate(), Is.EqualTo("second"));
        });
    }

    [Test]
    public void GetSessionLog_FallsBackToLegacyUnscopedStore()
    {
        var context = CreateContext(new Dictionary<string, object?>(), "exec-a", "case-a");
        context.InsertValueIntoGlobalDictionary(
            ["__RunnerArtifacts", "SessionLogs"],
            new ConcurrentDictionary<string, ConcurrentQueue<string>>(StringComparer.Ordinal)
            {
                ["legacy-session"] = new ConcurrentQueue<string>(["legacy-line"])
            });

        Assert.That(context.GetSessionLog("legacy-session"), Does.Contain("legacy-line"));
    }

    [Test]
    public void AppendSessionLog_WhenScopedStoreAlreadyExists_ReusesExistingStore()
    {
        var sharedGlobalDict = new Dictionary<string, object?>();
        var existingStore = new ConcurrentDictionary<string, ConcurrentQueue<string>>(StringComparer.Ordinal)
        {
            ["session-a"] = new ConcurrentQueue<string>(["first-line"])
        };
        var context = CreateContext(sharedGlobalDict, "exec-a", "case-a");
        context.InsertValueIntoGlobalDictionary(
            ["__RunnerArtifacts", "Scoped", "exec-a::case-a", "SessionLogs"],
            existingStore);

        context.AppendSessionLog("session-a", "second-line");
        var storedStore = context.GetValueFromGlobalDictionary(["__RunnerArtifacts", "Scoped", "exec-a::case-a", "SessionLogs"])
            as ConcurrentDictionary<string, ConcurrentQueue<string>>;

        Assert.Multiple(() =>
        {
            Assert.That(context.GetSessionLog("session-a"), Does.Contain("first-line"));
            Assert.That(context.GetSessionLog("session-a"), Does.Contain("second-line"));
            Assert.That(storedStore, Is.SameAs(existingStore));
        });
    }

    [Test]
    public void GetSessionLog_WhenStoredQueueIsEmpty_ReturnsNull()
    {
        var context = CreateContext(new Dictionary<string, object?>(), "exec-a", "case-a");
        context.InsertValueIntoGlobalDictionary(
            ["__RunnerArtifacts", "Scoped", "exec-a::case-a", "SessionLogs"],
            new ConcurrentDictionary<string, ConcurrentQueue<string>>(StringComparer.Ordinal)
            {
                ["session-a"] = new ConcurrentQueue<string>()
            });

        Assert.That(context.GetSessionLog("session-a"), Is.Null);
    }

    [Test]
    public void RenderedTemplates_WithOnlyExecutionIdOrCaseName_RemainScoped()
    {
        var sharedGlobalDict = new Dictionary<string, object?>();
        var executionOnlyContext = CreateContext(sharedGlobalDict, "exec-a", null!);
        var caseOnlyContext = CreateContext(sharedGlobalDict, null!, "case-a");

        executionOnlyContext.SetRenderedConfigurationTemplate("execution-only");
        caseOnlyContext.SetRenderedConfigurationTemplate("case-only");

        Assert.Multiple(() =>
        {
            Assert.That(executionOnlyContext.GetRenderedConfigurationTemplate(), Is.EqualTo("execution-only"));
            Assert.That(caseOnlyContext.GetRenderedConfigurationTemplate(), Is.EqualTo("case-only"));
        });
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
