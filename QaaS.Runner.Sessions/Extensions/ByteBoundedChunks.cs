using System.Text;
using System.Text.Json;
using QaaS.Framework.SDK.Session.DataObjects;

namespace QaaS.Runner.Sessions.Extensions;

internal static class ByteBoundedChunks
{
    internal static IEnumerable<(Data<object> Original, Data<object> Serialized)[]> Create(
        IEnumerable<(Data<object> Original, Data<object> Serialized)> source,
        int count,
        long? maxBytes
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        if (maxBytes is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (!maxBytes.HasValue)
            return source.Chunk(count);
        return Enumerate(source, count, maxBytes.Value);
    }

    private static IEnumerable<(Data<object> Original, Data<object> Serialized)[]> Enumerate(
        IEnumerable<(Data<object> Original, Data<object> Serialized)> source,
        int count,
        long limit
    )
    {
        var chunk = new List<(Data<object>, Data<object>)>();
        long bytes = 0;
        foreach (var item in source)
        {
            var size = BodySize(item.Serialized.Body);
            if (size > limit)
                throw new InvalidOperationException(
                    $"One serialized message body ({size} bytes) exceeds Chunk.MaxBytes ({limit})."
                );
            if (chunk.Count > 0 && (chunk.Count == count || size > limit - bytes))
            {
                yield return chunk.ToArray();
                chunk.Clear();
                bytes = 0;
            }
            chunk.Add(item);
            bytes += size;
        }
        if (chunk.Count > 0)
            yield return chunk.ToArray();
    }

    // This is a payload budget, not the transport's encoded request size or total retained memory.
    private static long BodySize(object? body) =>
        body switch
        {
            null => 0,
            byte[] bytes => bytes.LongLength,
            ReadOnlyMemory<byte> bytes => bytes.Length,
            Memory<byte> bytes => bytes.Length,
            string text => Encoding.UTF8.GetByteCount(text),
            _ => JsonSerializer.SerializeToUtf8Bytes(body).LongLength,
        };
}
