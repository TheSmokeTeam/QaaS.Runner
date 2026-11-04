using NUnit.Framework;
using QaaS.Framework.Policies.Exceptions;
using QaaS.Runner.Sessions.Extensions;

namespace QaaS.Runner.Sessions.Tests.Extensions;

[TestFixture]
public class BoundedIterationTests
{
    [Test]
    public void DegreeOne_DoesNotPrefetchPastTheCurrentItem()
    {
        var completed = 0;
        IEnumerable<int> Source()
        {
            for (var i = 0; i < 100; i++)
            {
                Assert.That(completed, Is.EqualTo(i));
                yield return i;
            }
        }
        var iterator = new IterableSerializableDataIterator(null, null);
        iterator.ApplyToAll(Source(), _ => completed++, true, 1);
        Assert.That(completed, Is.EqualTo(100));
    }

    [Test]
    public void InvalidDegree_IsRejectedBeforeEnumerating()
    {
        var iterator = new IterableSerializableDataIterator(null, null);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            iterator.ApplyToAll(Array.Empty<int>(), _ => { }, true, 0)
        );
    }

    [Test]
    public void DegreeFour_BoundsInputReadAheadAsWellAsActiveCallbacks()
    {
        var completed = 0;
        IEnumerable<int> Source()
        {
            for (var i = 0; i < 2000; i++)
            {
                Assert.That(i + 1 - Volatile.Read(ref completed), Is.LessThanOrEqualTo(4));
                yield return i;
            }
        }
        var iterator = new IterableSerializableDataIterator(null, null);
        iterator.ApplyToAll(Source(), _ => Interlocked.Increment(ref completed), true, 4);
        Assert.That(completed, Is.EqualTo(2000));
    }

    [Test]
    public void ConcurrentPolicyStop_DoesNotHideTransportFailure()
    {
        using var callbacks = new Barrier(2);
        var iterator = new IterableSerializableDataIterator(null, null);
        var error = Assert.Throws<AggregateException>(() =>
            iterator.ApplyToAll(
                new[] { 0, 1 },
                item =>
                {
                    if (!callbacks.SignalAndWait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("The parallel callbacks did not both start.");
                    if (item == 0)
                        throw new StopActionException("done");
                    throw new IOException("transport failed");
                },
                true,
                2
            )
        );
        Assert.That(
            error!.Flatten().InnerExceptions.Select(exception => exception.GetType()),
            Is.EquivalentTo(new[] { typeof(StopActionException), typeof(IOException) })
        );
    }

    [Test]
    public void PolicyStop_IsNormalControlFlow_ButTransportFailuresAreNotSwallowed()
    {
        var iterator = new IterableSerializableDataIterator(null, null);
        Assert.Throws<StopActionException>(() =>
            iterator.ApplyToAll(new[] { 1 }, _ => throw new StopActionException("done"), true, 1)
        );
        var failure = Assert.Throws<AggregateException>(() =>
            iterator.ApplyToAll(
                new[] { 1 },
                _ => throw new IOException("transport failed"),
                true,
                1
            )
        );
        Assert.That(failure!.Flatten().InnerExceptions.Single(), Is.TypeOf<IOException>());
    }
}
