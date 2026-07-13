using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session.CommunicationDataObjects;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Framework.Serialization;
using QaaS.Runner.Sessions.Extensions;
using QaaS.Runner.Sessions.Tests.Actions.Utils;
using InternalCommunicationData = QaaS.Runner.Sessions.Actions.InternalCommunicationData<object>;
using SessionAction = QaaS.Runner.Sessions.Actions.Action;

namespace QaaS.Runner.Sessions.Tests.Extensions;

[TestFixture]
public class SessionExtensionsTests
{
    private const string SessionName = "TestSession";

    [Test]
    public void DisposeOfEnumerable_WithNullEnumerable_DoesNotThrow()
    {
        IEnumerable<DisposableTracker>? disposables = null;

        Assert.DoesNotThrow(() =>
            disposables.DisposeOfEnumerable("DisposableTracker", Globals.Logger)
        );
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

        failures.AppendActionFailure(
            new InvalidOperationException("Action failed"),
            SessionName,
            Globals.Logger,
            "Publisher",
            "PublishAction",
            "Kafka"
        );

        Assert.That(failures, Has.Count.EqualTo(1));
        Assert.That(failures[0].Name, Is.EqualTo("PublishAction"));
        Assert.That(failures[0].ActionType, Is.EqualTo("Publisher"));
        Assert.That(failures[0].Reason.Message, Is.EqualTo("Action failed"));
    }

    [Test]
    public void AppendActionFailure_ForList_WithoutProtocol_AppendsFailure()
    {
        var failures = new List<ActionFailure>();

        failures.AppendActionFailure(
            new InvalidOperationException("No protocol"),
            SessionName,
            Globals.Logger,
            "Collector",
            "CollectAction"
        );

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
            exceptionMessage: "custom message"
        );

        Assert.That(failures, Has.Count.EqualTo(1));
        Assert.That(failures.First().Reason.Message, Is.EqualTo("custom message"));
    }

    [Test]
    public void AppendActionFailure_ForConcurrentBag_FormatsS3LikeExceptionsCompactly()
    {
        var failures = new ConcurrentBag<ActionFailure>();
        var exception = new FakeAmazonS3Exception("request timed out")
        {
            StatusCode = HttpStatusCode.ServiceUnavailable,
            ErrorCode = "RequestTimeout",
            RequestId = "req-123",
        };

        failures.AppendActionFailure(
            exception,
            SessionName,
            Globals.Logger,
            "Consumer",
            "S3Consumer",
            actionProtocol: "S3"
        );

        var reason = failures.Single().Reason;
        Assert.Multiple(() =>
        {
            Assert.That(reason.Message, Does.Contain("S3 operation failed"));
            Assert.That(reason.Message, Does.Contain("StatusCode=503 ServiceUnavailable"));
            Assert.That(reason.Message, Does.Contain("ErrorCode=RequestTimeout"));
            Assert.That(reason.Message, Does.Not.Contain("RequestId"));
            Assert.That(reason.Description, Does.Contain("ExceptionType: FakeAmazonS3Exception"));
            Assert.That(reason.Description, Does.Contain("RequestId: req-123"));
            Assert.That(reason.Description, Does.Not.Contain("   at "));
        });
    }

    [Test]
    public void AppendActionFailure_ForNestedAggregateException_DeduplicatesRepeatedS3Causes()
    {
        var failures = new List<ActionFailure>();
        var first = CreateS3Failure("repeated object read failure");
        var second = CreateS3Failure("repeated object read failure");
        var exception = new AggregateException(
            new AggregateException(first),
            new AggregateException(second, second)
        );

        failures.AppendActionFailure(
            exception,
            SessionName,
            Globals.Logger,
            "ChunkConsumer",
            "S3Consumer",
            actionProtocol: "S3"
        );

        Assert.That(failures, Has.Count.EqualTo(1));
        var reason = failures.Single().Reason;
        Assert.Multiple(() =>
        {
            Assert.That(reason.Message, Does.StartWith("S3 operation failed:"));
            Assert.That(reason.Message, Does.Contain("repeated object read failure"));
            Assert.That(reason.Message, Does.Not.Contain("One or more errors occurred"));
            Assert.That(reason.Message, Does.Not.Contain(") ("));
            Assert.That(
                CountOccurrences(reason.Message, "repeated object read failure"),
                Is.EqualTo(1)
            );
            Assert.That(
                CountOccurrences(reason.Description, "repeated object read failure"),
                Is.EqualTo(1)
            );
            Assert.That(reason.Description, Does.Not.Contain("Inner Exception #"));
        });
    }

    [Test]
    public void AppendActionFailure_ForS3AggregateWithDifferentRequestIds_DeduplicatesSemantically()
    {
        var firstOrderFailures = new List<ActionFailure>();
        var reverseOrderFailures = new List<ActionFailure>();
        var first = CreateS3Failure(
            "repeated object read failure",
            requestId: "request-z",
            amazonId2: "host-z"
        );
        var second = CreateS3Failure(
            "repeated object read failure",
            requestId: "request-a",
            amazonId2: "host-a"
        );

        firstOrderFailures.AppendActionFailure(
            new AggregateException(first, second),
            SessionName,
            Globals.Logger,
            "ChunkConsumer",
            "S3Consumer",
            actionProtocol: "S3"
        );
        reverseOrderFailures.AppendActionFailure(
            new AggregateException(second, first),
            SessionName,
            Globals.Logger,
            "ChunkConsumer",
            "S3Consumer",
            actionProtocol: "S3"
        );

        const string expectedMessage =
            "S3 operation failed: StatusCode=404 NotFound, ErrorCode=NoSuchKey, Message=repeated object read failure";
        Assert.Multiple(() =>
        {
            Assert.That(firstOrderFailures, Has.Count.EqualTo(1));
            Assert.That(reverseOrderFailures, Has.Count.EqualTo(1));
            Assert.That(firstOrderFailures.Single(), Is.EqualTo(reverseOrderFailures.Single()));
            Assert.That(firstOrderFailures.Single().Reason.Message, Is.EqualTo(expectedMessage));
            Assert.That(firstOrderFailures.Single().Reason.Message, Does.Not.Contain("RequestId"));
            Assert.That(firstOrderFailures.Single().Reason.Message, Does.Not.Contain("AmazonId2"));
            Assert.That(
                firstOrderFailures.Single().Reason.Message,
                Does.Not.Contain("One or more errors occurred")
            );
            Assert.That(firstOrderFailures.Single().Reason.Message, Does.Not.Contain(") ("));
            Assert.That(
                firstOrderFailures.Single().Reason.Description,
                Does.Contain("RequestId: request-a")
            );
            Assert.That(
                firstOrderFailures.Single().Reason.Description,
                Does.Contain("AmazonId2: host-a")
            );
            Assert.That(
                firstOrderFailures.Single().Reason.Description,
                Does.Not.Contain("request-z")
            );
        });
    }

    [Test]
    public void AppendActionFailure_ForAggregateException_PreservesDistinctCausesInStableOrder()
    {
        var failures = new List<ActionFailure>();
        var exception = new AggregateException(
            new InvalidOperationException("zeta"),
            new AggregateException(
                new ArgumentException("alpha"),
                new InvalidOperationException("zeta")
            )
        );

        failures.AppendActionFailure(
            exception,
            SessionName,
            Globals.Logger,
            "Publisher",
            "PublishAction"
        );

        Assert.Multiple(() =>
        {
            Assert.That(failures, Has.Count.EqualTo(2));
            Assert.That(
                failures.Select(failure => failure.Reason.Message),
                Is.EqualTo(new[] { "alpha", "zeta" })
            );
            Assert.That(
                failures.Select(failure => failure.Reason.Description),
                Has.All.Not.Contain("One or more errors occurred")
            );
        });
    }

    [Test]
    public void NormalizeActionFailures_DeduplicatesExactFailuresAndSortsDeterministically()
    {
        var repeated = new ActionFailure
        {
            Name = "action-z",
            ActionType = "Consumer",
            Reason = new Reason { Message = "same", Description = "same description" },
        };
        var distinct = repeated with
        {
            Name = "action-a",
            Reason = new Reason { Message = "different", Description = "different description" },
        };

        var normalized = ActionFailureNormalizer.Normalize([repeated, distinct, repeated]);

        Assert.Multiple(() =>
        {
            Assert.That(normalized, Has.Count.EqualTo(2));
            Assert.That(
                normalized.Select(failure => failure.Name),
                Is.EqualTo(new[] { "action-a", "action-z" })
            );
        });
    }

    [Test]
    public void NormalizeActionFailures_CollapsesStructuredS3FailuresWithDifferentDiagnostics()
    {
        var first = new ActionFailure
        {
            Name = "S3Consumer",
            ActionType = "ChunkConsumer",
            Reason = new Reason
            {
                Message =
                    "S3 operation failed: StatusCode=404 NotFound, ErrorCode=NoSuchKey, RequestId=request-z, AmazonId2=host-z, Message=missing object",
                Description = "RequestId: request-z\nAmazonId2: host-z",
            },
        };
        var second = first with
        {
            Reason = new Reason
            {
                Message =
                    "S3 operation failed: StatusCode=404 NotFound, ErrorCode=NoSuchKey, RequestId=request-a, AmazonId2=host-a, Message=missing object",
                Description = "RequestId: request-a\nAmazonId2: host-a",
            },
        };

        var normalized = ActionFailureNormalizer.Normalize([first, second]);

        Assert.Multiple(() =>
        {
            Assert.That(normalized, Has.Count.EqualTo(1));
            Assert.That(
                normalized.Single().Reason.Message,
                Is.EqualTo(
                    "S3 operation failed: StatusCode=404 NotFound, ErrorCode=NoSuchKey, Message=missing object"
                )
            );
            Assert.That(normalized.Single().Reason.Description, Does.Contain("request-a"));
            Assert.That(normalized.Single().Reason.Description, Does.Contain("host-a"));
        });
    }

    [Test]
    public void InternalCommunicationData_InheritsCommunicationDataContract_ForInputData()
    {
        var input = new List<DetailedData<string>> { new() { Body = "input-body" } };
        var output = new List<DetailedData<string>?> { new() { Body = "output-body" } };
        var internalCommunicationData =
            new QaaS.Runner.Sessions.Actions.InternalCommunicationData<string>
            {
                Input = input,
                Output = output,
                InputSerializationType = SerializationType.Json,
                OutputSerializationType = SerializationType.Binary,
            };

        CommunicationData<string> communicationData = internalCommunicationData;

        Assert.That(communicationData.Data, Is.SameAs(input));
        Assert.That(communicationData.SerializationType, Is.EqualTo(SerializationType.Json));
        Assert.That(internalCommunicationData.Input, Is.SameAs(input));
        Assert.That(internalCommunicationData.Output, Is.SameAs(output));
        Assert.That(internalCommunicationData.Output![0]!.Body, Is.EqualTo("output-body"));
    }

    [Test]
    public void CreateTaskFromAction_WhenActionSucceeds_ReturnsActionAndData()
    {
        var context = CreationalFunctions.CreateContext(SessionName, []);
        var failures = new ConcurrentBag<ActionFailure>();
        var action = new SuccessfulAction("SuccessfulAction");

        var task = SessionExtensions.CreateTaskFromAction(context, action, SessionName, failures);
        task.GetAwaiter().GetResult();

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
        var action = new ExceptionalAction(
            "ExceptionalAction",
            new InvalidOperationException("boom")
        );

        var task = SessionExtensions.CreateTaskFromAction(context, action, SessionName, failures);
        task.GetAwaiter().GetResult();

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
        task.GetAwaiter().GetResult();

        Assert.That(task.Result, Is.Null);
        Assert.That(failures, Has.Count.EqualTo(1));
        Assert.That(
            failures.First().Reason.Message,
            Is.EqualTo("Action CanceledAction was canceled")
        );
    }

    [Test]
    public void CreateTaskFromAction_WhenOperationIsCanceled_CancelsMatchingRunningData()
    {
        var context = CreationalFunctions.CreateContext(SessionName, []);
        var failures = new ConcurrentBag<ActionFailure>();
        var action = new ExceptionalAction("CancelableAction", new OperationCanceledException());
        var inputRcd = new RunningCommunicationData<object> { Name = action.Name };
        var outputRcd = new RunningCommunicationData<object> { Name = action.Name };
        context.AddRunningInputData(SessionName, inputRcd);
        context.AddRunningOutputData(SessionName, outputRcd);

        var task = SessionExtensions.CreateTaskFromAction(context, action, SessionName, failures);
        task.GetAwaiter().GetResult();

        Assert.That(inputRcd.DataCancellationTokenSource.IsCancellationRequested, Is.True);
        Assert.That(outputRcd.DataCancellationTokenSource.IsCancellationRequested, Is.True);
    }

    [Test]
    public void CreateTaskFromAction_WhenRunningSessionEntryIsMissing_AppendsOriginalFailureWithoutMaskingException()
    {
        var context = new InternalContext
        {
            Logger = Globals.Logger,
            InternalRunningSessions = new RunningSessions(
                new Dictionary<string, RunningSessionData<object, object>>()
            ),
        };
        var failures = new ConcurrentBag<ActionFailure>();
        var action = new ExceptionalAction(
            "MissingSessionAction",
            new InvalidOperationException("boom")
        );

        var task = SessionExtensions.CreateTaskFromAction(context, action, SessionName, failures);

        Assert.DoesNotThrow(() => task.GetAwaiter().GetResult());
        Assert.That(task.Result, Is.Null);
        Assert.That(failures, Has.Count.EqualTo(1));
        Assert.That(failures.First().Reason.Message, Is.EqualTo("boom"));
    }

    [Test]
    public void RunningSessionHelpers_SetGetAndRemoveSessionData()
    {
        var context = CreationalFunctions.CreateContext(SessionName, []);
        var runningSession = new RunningSessionData<object, object> { Inputs = [], Outputs = [] };

        context.SetRunningSession("other-session", runningSession);

        Assert.That(
            context.TryGetRunningSession("other-session", out var foundRunningSession),
            Is.True
        );
        Assert.That(foundRunningSession, Is.SameAs(runningSession));
        Assert.That(context.GetRunningSession("other-session"), Is.SameAs(runningSession));
        Assert.That(context.RemoveRunningSession("other-session"), Is.True);
        Assert.That(context.TryGetRunningSession("other-session", out _), Is.False);
    }

    [Test]
    public void RunningSessionHelpers_AddRunningCommunicationData_AppendsToInputsAndOutputs()
    {
        var context = CreationalFunctions.CreateContext(SessionName, []);
        var input = new RunningCommunicationData<object> { Name = "input" };
        var output = new RunningCommunicationData<object> { Name = "output" };

        context.AddRunningInputData(SessionName, input);
        context.AddRunningOutputData(SessionName, output);

        var runningSession = context.GetRunningSession(SessionName);
        Assert.That(runningSession.Inputs, Contains.Item(input));
        Assert.That(runningSession.Outputs, Contains.Item(output));
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
                Output = [new DetailedData<object> { Body = "ok" }],
            };
        }
    }

    private sealed class ExceptionalAction(string name, Exception exceptionToThrow)
        : SessionAction(name, Globals.Logger)
    {
        internal override InternalCommunicationData Act()
        {
            throw exceptionToThrow;
        }
    }

    private sealed class FakeAmazonS3Exception(string message) : Exception(message)
    {
        public HttpStatusCode StatusCode { get; init; }
        public string? ErrorCode { get; init; }
        public string? RequestId { get; init; }
        public string? AmazonId2 { get; init; }
    }

    private static FakeAmazonS3Exception CreateS3Failure(
        string message,
        string requestId = "same-request",
        string amazonId2 = "same-host"
    ) =>
        new(message)
        {
            StatusCode = HttpStatusCode.NotFound,
            ErrorCode = "NoSuchKey",
            RequestId = requestId,
            AmazonId2 = amazonId2,
        };

    private static int CountOccurrences(string value, string expected) =>
        value.Split(expected, StringSplitOptions.None).Length - 1;
}
