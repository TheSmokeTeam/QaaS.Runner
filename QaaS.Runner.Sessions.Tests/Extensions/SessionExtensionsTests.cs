using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QaaS.Framework.SDK.Session.CommunicationDataObjects;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Runner.Sessions.Extensions;
using QaaS.Runner.Sessions.Tests.Actions.Utils;
using SessionAction = QaaS.Runner.Sessions.Actions.Action;
using InternalCommunicationData = QaaS.Runner.Sessions.Actions.InternalCommunicationData<object>;

namespace QaaS.Runner.Sessions.Tests.Extensions;

[TestFixture]
public class SessionExtensionsTests
{
    private const string SessionName = "TestSession";

    [Test]
    public void DisposeOfEnumerable_WithNullEnumerable_DoesNotThrow()
    {
        IEnumerable<DisposableTracker>? disposables = null;

        Assert.DoesNotThrow(() => disposables.DisposeOfEnumerable("DisposableTracker", Globals.Logger));
    }

    [Test]
    public void DisposeOfEnumerable_WithItems_DisposesAllItems()
    {
        var first = new DisposableTracker();
        var second = new DisposableTracker();
        var disposables = new List<DisposableTracker> { first, second };

        disposables.DisposeOfEnumerable("DisposableTracker", Globals.Logger);

        Assert.That(first.IsDisposed, Is.True);
        Assert.That(second.IsDisposed, Is.True);
    }

    [Test]
    public void AppendActionFailure_ForList_AppendsFailureWithExceptionMessage()
    {
        var failures = new List<ActionFailure>();

        failures.AppendActionFailure(new InvalidOperationException("Action failed"), SessionName, Globals.Logger,
            "Publisher", "PublishAction", "Kafka");

        Assert.That(failures, Has.Count.EqualTo(1));
        Assert.That(failures[0].Name, Is.EqualTo("PublishAction"));
        Assert.That(failures[0].ActionType, Is.EqualTo("Publisher"));
        Assert.That(failures[0].Reason.Message, Is.EqualTo("Action failed"));
    }

    [Test]
    public void AppendActionFailure_ForList_WithoutProtocol_AppendsFailure()
    {
        var failures = new List<ActionFailure>();

        failures.AppendActionFailure(new InvalidOperationException("No protocol"), SessionName, Globals.Logger,
            "Collector", "CollectAction");

        Assert.That(failures, Has.Count.EqualTo(1));
        Assert.That(failures[0].Name, Is.EqualTo("CollectAction"));
        Assert.That(failures[0].Reason.Message, Is.EqualTo("No protocol"));
    }

    [Test]
    public void AppendActionFailure_ForConcurrentBag_UsesProvidedExceptionMessage()
    {
        var failures = new ConcurrentBag<ActionFailure>();

        failures.AppendActionFailure(
            new InvalidOperationException("original message"),
            SessionName,
            Globals.Logger,
            "Consumer",
            "ConsumeAction",
            actionProtocol: "Kafka",
            exceptionMessage: "custom message");

        Assert.That(failures, Has.Count.EqualTo(1));
        Assert.That(failures.First().Reason.Message, Is.EqualTo("custom message"));
    }

    [Test]
    public void CreateTaskFromAction_WhenActionSucceeds_ReturnsActionAndData()
    {
        var context = CreationalFunctions.CreateContext(SessionName, []);
        var failures = new ConcurrentBag<ActionFailure>();
        var action = new SuccessfulAction("SuccessfulAction");

        var task = SessionExtensions.CreateTaskFromAction(context, action, SessionName, failures);
        task.RunSynchronously();

        Assert.That(task.Result, Is.Not.Null);
        Assert.That(task.Result!.Item1, Is.SameAs(action));
        Assert.That(task.Result.Item2.Output, Is.Not.Null);
        Assert.That(failures, Is.Empty);
    }

    [Test]
    public void CreateTaskFromAction_WhenActionThrows_AppendsFailureAndReturnsNull()
    {
        var context = CreationalFunctions.CreateContext(SessionName, []);
        var failures = new ConcurrentBag<ActionFailure>();
        var action = new ExceptionalAction("ExceptionalAction", new InvalidOperationException("boom"));

        var task = SessionExtensions.CreateTaskFromAction(context, action, SessionName, failures);
        task.RunSynchronously();

        Assert.That(task.Result, Is.Null);
        Assert.That(failures, Has.Count.EqualTo(1));
        Assert.That(failures.First().Reason.Message, Is.EqualTo("boom"));
    }

    [Test]
    public void CreateTaskFromAction_WhenOperationIsCanceled_AppendsCancellationFailure()
    {
        var context = CreationalFunctions.CreateContext(SessionName, []);
        var failures = new ConcurrentBag<ActionFailure>();
        var action = new ExceptionalAction("CanceledAction", new OperationCanceledException());

        var task = SessionExtensions.CreateTaskFromAction(context, action, SessionName, failures);
        task.RunSynchronously();

        Assert.That(task.Result, Is.Null);
        Assert.That(failures, Has.Count.EqualTo(1));
        Assert.That(failures.First().Reason.Message, Is.EqualTo("Action CanceledAction was canceled"));
    }

    [Test]
    public void CreateTaskFromAction_WhenOperationIsCanceled_CancelsMatchingRunningData()
    {
        var context = CreationalFunctions.CreateContext(SessionName, []);
        var failures = new ConcurrentBag<ActionFailure>();
        var action = new ExceptionalAction("CancelableAction", new OperationCanceledException());
        var inputRcd = new RunningCommunicationData<object> { Name = action.Name };
        var outputRcd = new RunningCommunicationData<object> { Name = action.Name };
        context.InternalRunningSessions.RunningSessionsDict[SessionName].Inputs!.Add(inputRcd);
        context.InternalRunningSessions.RunningSessionsDict[SessionName].Outputs!.Add(outputRcd);

        var task = SessionExtensions.CreateTaskFromAction(context, action, SessionName, failures);
        task.RunSynchronously();

        Assert.That(inputRcd.DataCancellationTokenSource.IsCancellationRequested, Is.True);
        Assert.That(outputRcd.DataCancellationTokenSource.IsCancellationRequested, Is.True);
    }

    [Test]
    public void RunningSessionHelpers_SetGetAndRemoveSessionData()
    {
        var context = CreationalFunctions.CreateContext(SessionName, []);
        var runningSession = new RunningSessionData<object, object>
        {
            Inputs = [],
            Outputs = []
        };

        context.SetRunningSession("other-session", runningSession);

        Assert.That(context.GetRunningSession("other-session"), Is.SameAs(runningSession));
        Assert.That(context.RemoveRunningSession("other-session"), Is.True);
    }

    private sealed class DisposableTracker : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class SuccessfulAction(string name) : SessionAction(name, Globals.Logger)
    {
        internal override InternalCommunicationData Act()
        {
            return new InternalCommunicationData
            {
                Output = [new DetailedData<object> { Body = "ok" }]
            };
        }
    }

    private sealed class ExceptionalAction(string name, Exception exceptionToThrow) : SessionAction(name, Globals.Logger)
    {
        internal override InternalCommunicationData Act()
        {
            throw exceptionToThrow;
        }
    }
}
