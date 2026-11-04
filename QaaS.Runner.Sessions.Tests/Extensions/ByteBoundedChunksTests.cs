using NUnit.Framework;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Runner.Sessions.Extensions;

namespace QaaS.Runner.Sessions.Tests.Extensions;

[TestFixture]
public class ByteBoundedChunksTests
{
    private static IEnumerable<(Data<object>, Data<object>)> Bodies(params object[] bodies) =>
        bodies.Select(body => (new Data<object> { Body = body }, new Data<object> { Body = body }));

    [Test]
    public void CountAndBytes_BothBoundEachChunk_WithoutChangingOrder()
    {
        var chunks = ByteBoundedChunks
            .Create(Bodies(new byte[6], new byte[4], new byte[5], new byte[2]), 2, 10)
            .ToArray();
        Assert.That(chunks.Select(x => x.Length), Is.EqualTo(new[] { 2, 2 }));
        Assert.That(
            chunks.SelectMany(x => x).Select(x => ((byte[])x.Serialized.Body!).Length),
            Is.EqualTo(new[] { 6, 4, 5, 2 })
        );
    }

    [Test]
    public void Strings_UseUtf8ByteCount_AndOversizedItemsFail()
    {
        Assert.That(
            ByteBoundedChunks.Create(Bodies("é", "é"), 10, 3).Select(x => x.Length),
            Is.EqualTo(new[] { 1, 1 })
        );
        Assert.Throws<InvalidOperationException>(() =>
            ByteBoundedChunks.Create(Bodies(new byte[4]), 10, 3).ToArray()
        );
    }

    [Test]
    public void UnconfiguredLimit_PreservesCountOnlyBehavior()
    {
        Assert.That(
            ByteBoundedChunks
                .Create(Bodies(new byte[10], new byte[10], new byte[10]), 2, null)
                .Select(x => x.Length),
            Is.EqualTo(new[] { 2, 1 })
        );
    }
}
