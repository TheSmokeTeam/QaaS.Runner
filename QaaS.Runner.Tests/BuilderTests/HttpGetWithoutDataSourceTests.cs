using Moq;
using NUnit.Framework;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Runner.Sessions.RuntimeOverrides;

namespace QaaS.Runner.Tests.BuilderTests;

[TestFixture]
public class HttpGetWithoutDataSourceTests
{
    [Test]
    public void Build_FromYaml_WithExplicitEmptyHttpGet_DoesNotRequireDataSourceSelectors()
    {
        using var runner = Bootstrap.New([
            "act",
            "TestData/http-get-empty-request.qaas.yaml",
            "--no-process-exit",
            "--no-env",
            "--send-logs",
            "false",
        ]);

        using var execution = runner.ExecutionBuilders.Single().Build();

        Assert.That(execution, Is.Not.Null);
    }

    [Test]
    public void Start_FromYaml_WithExplicitEmptyHttpGet_TransactsExactlyOnce()
    {
        Data<object>? capturedRequest = null;
        var transactor = new Mock<ITransactor>();
        transactor
            .Setup(instance => instance.Transact(It.IsAny<Data<object>>()))
            .Returns<Data<object>>(request =>
            {
                capturedRequest = request;
                return new Tuple<DetailedData<object>, DetailedData<object>?>(
                    request.CloneDetailed(),
                    null
                );
            });
        var overrideContext = new InternalContext
        {
            InternalGlobalDict = new Dictionary<string, object?>(),
        };
        overrideContext.SetSessionActionOverrides(
            new SessionActionOverrides { Transaction = _ => transactor.Object }
        );
        using var runner = Bootstrap.New([
            "act",
            "TestData/http-get-empty-request.qaas.yaml",
            "--no-process-exit",
            "--no-env",
            "--send-logs",
            "false",
        ]);
        var builder = runner.ExecutionBuilders.Single();
        builder.WithGlobalDict(overrideContext.InternalGlobalDict);

        using var execution = builder.Build();
        var exitCode = execution.Start();

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero);
            transactor.Verify(instance => instance.Transact(It.IsAny<Data<object>>()), Times.Once);
            Assert.That(capturedRequest, Is.Not.Null);
            Assert.That(capturedRequest!.Body, Is.TypeOf<byte[]>());
            Assert.That((byte[])capturedRequest.Body!, Is.Empty);
        });
    }
}
