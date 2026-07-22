using System;
using System.Linq;
using NUnit.Framework;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.Assertions.Tests;

[TestFixture]
public class ActionFailureNormalizerTests
{
    [Test]
    public void Normalize_CanonicalizesEquivalentWhitespaceIndependentlyOfInputOrder()
    {
        var nonS3CrLf = new ActionFailure
        {
            Name = "Publisher",
            ActionType = "Publisher",
            Reason = new Reason
            {
                Message = "  publish failed\r\n",
                Description = "  first line\r\nsecond line  ",
            },
        };
        var nonS3Lf = nonS3CrLf with
        {
            Reason = new Reason
            {
                Message = "publish failed",
                Description = "first line\nsecond line",
            },
        };
        var s3CrLf = new ActionFailure
        {
            Name = "S3Consumer",
            ActionType = "ChunkConsumer",
            Reason = new Reason
            {
                Message =
                    "  S3 operation failed: StatusCode=404 NotFound, ErrorCode=NoSuchKey, Message=missing object\r\n",
                Description = "  RequestId: request-a\r\nAmazonId2: host-a  ",
            },
        };
        var s3Lf = s3CrLf with
        {
            Reason = new Reason
            {
                Message =
                    "S3 operation failed: StatusCode=404 NotFound, ErrorCode=NoSuchKey, Message=missing object",
                Description = "RequestId: request-a\nAmazonId2: host-a",
            },
        };

        var forward = ActionFailureNormalizer.Normalize([nonS3CrLf, s3Lf, nonS3Lf, s3CrLf]);
        var reverse = ActionFailureNormalizer.Normalize([s3CrLf, nonS3Lf, s3Lf, nonS3CrLf]);

        Assert.Multiple(() =>
        {
            Assert.That(forward, Is.EqualTo(reverse));
            Assert.That(forward, Has.Count.EqualTo(2));
            Assert.That(
                forward.Single(failure => failure.Name == "Publisher").Reason,
                Is.EqualTo(
                    new Reason
                    {
                        Message = "publish failed",
                        Description = "first line\nsecond line",
                    }
                )
            );
            Assert.That(
                forward.Single(failure => failure.Name == "S3Consumer").Reason,
                Is.EqualTo(
                    new Reason
                    {
                        Message =
                            "S3 operation failed: StatusCode=404 NotFound, ErrorCode=NoSuchKey, Message=missing object",
                        Description = "RequestId: request-a\nAmazonId2: host-a",
                    }
                )
            );
        });
    }

    [Test]
    public void Normalize_CollapsesSemanticS3DuplicatesAndPreservesDistinctNonS3Causes()
    {
        var firstS3Failure = CreateS3Failure("request-z", "host-z");
        var secondS3Failure = CreateS3Failure("request-a", "host-a");
        var firstNonS3Failure = CreateNonS3Failure("cause-a");
        var secondNonS3Failure = CreateNonS3Failure("cause-b");

        var normalized = ActionFailureNormalizer.Normalize([
            firstS3Failure,
            secondNonS3Failure,
            secondS3Failure,
            firstNonS3Failure,
        ]);
        var reversed = ActionFailureNormalizer.Normalize([
            firstNonS3Failure,
            secondS3Failure,
            secondNonS3Failure,
            firstS3Failure,
        ]);

        var s3Failure = normalized.Single(failure =>
            failure.Reason.Message.StartsWith("S3 operation failed:", StringComparison.Ordinal)
        );
        Assert.Multiple(() =>
        {
            Assert.That(normalized, Has.Count.EqualTo(3));
            Assert.That(normalized, Is.EqualTo(reversed));
            Assert.That(
                s3Failure.Reason.Message,
                Is.EqualTo(
                    "S3 operation failed: StatusCode=404 NotFound, ErrorCode=NoSuchKey, Message=missing object"
                )
            );
            Assert.That(s3Failure.Reason.Message, Does.Not.Contain("RequestId"));
            Assert.That(s3Failure.Reason.Message, Does.Not.Contain("AmazonId2"));
            Assert.That(s3Failure.Reason.Description, Does.Contain("RequestId: request-a"));
            Assert.That(s3Failure.Reason.Description, Does.Contain("AmazonId2: host-a"));
            Assert.That(
                normalized
                    .Where(failure => failure.Reason.Message == "same non-S3 message")
                    .Select(failure => failure.Reason.Description),
                Is.EqualTo(new[] { "cause-a", "cause-b" })
            );
        });
    }

    private static ActionFailure CreateS3Failure(string requestId, string amazonId2) =>
        new()
        {
            Name = "S3Consumer",
            Action = "S3 S3Consumer",
            ActionType = "ChunkConsumer",
            Reason = new Reason
            {
                Message =
                    $"S3 operation failed: StatusCode=404 NotFound, ErrorCode=NoSuchKey, RequestId={requestId}, AmazonId2={amazonId2}, Message=missing object",
                Description =
                    $"ExceptionType: AmazonS3Exception\nMessage: missing object\nStatusCode: 404 NotFound\nErrorCode: NoSuchKey\nRequestId: {requestId}\nAmazonId2: {amazonId2}",
            },
        };

    private static ActionFailure CreateNonS3Failure(string description) =>
        new()
        {
            Name = "Publisher",
            Action = "Kafka Publisher",
            ActionType = "Publisher",
            Reason = new Reason { Message = "same non-S3 message", Description = description },
        };
}
