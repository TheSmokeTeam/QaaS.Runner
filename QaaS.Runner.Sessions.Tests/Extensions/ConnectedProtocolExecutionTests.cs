using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Runner.Sessions.Extensions;

namespace QaaS.Runner.Sessions.Tests.Extensions;

[TestFixture]
public class ConnectedProtocolExecutionTests
{
    [Test]
    public void DisconnectFailure_FailsSuccessfulWork_ButDoesNotReplaceTheOriginalFailure()
    {
        var protocol = new Mock<IConnectable>();
        protocol.Setup(x => x.Disconnect()).Throws(new TimeoutException("flush"));
        Assert.Throws<TimeoutException>(() =>
            ConnectedProtocolExecution.Run(protocol.Object, () => 1, NullLogger.Instance)
        );
        var original = new IOException("send failed");
        var observed = Assert.Throws<IOException>(() =>
            ConnectedProtocolExecution.Run<int>(
                protocol.Object,
                () => throw original,
                NullLogger.Instance
            )
        );
        Assert.That(observed, Is.SameAs(original));
        protocol.Verify(x => x.Disconnect(), Times.Exactly(2));
    }
}
