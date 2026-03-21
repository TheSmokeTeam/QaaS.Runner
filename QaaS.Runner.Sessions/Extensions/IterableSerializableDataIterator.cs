using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.Serialization.Serializers;

namespace QaaS.Runner.Sessions.Extensions;

/// <summary>
///     Wraps a data enumerable so when iterating over it its saved as is to a list and returns the data serialized
/// </summary>
public sealed class IterableSerializableDataIterator
{
    private readonly IEnumerable<Data<object>>? _iterableData;
    private readonly ISerializer? _serializer;

    public IterableSerializableDataIterator(IEnumerable<Data<object>>? iterableData, ISerializer? serializer)
    {
        IteratedData = new List<Data<object>>();
        _iterableData = iterableData;
        _serializer = serializer;
    }

    /// <summary>
    ///     Contains the iterated data before serialization
    /// </summary>
    public IList<Data<object>> IteratedData { get; }

    /// <summary>
    ///     Iterate over the iterable data loaded into this iterator while saving the iterated data as is in `IteratedData`
    ///     and returning the serialized data
    /// </summary>
    /// <returns> The iterable enumerable with its items serialized </returns>
    public IEnumerable<Data<object>> IterateEnumerable()
    {
        foreach (var (_, serialized) in IterateWithOriginal())
            yield return serialized;
    }

    /// <summary>
    ///     Iterate over the iterable data while returning both the original value and the serialized value.
    /// </summary>
    public IEnumerable<(Data<object> Original, Data<object> Serialized)> IterateWithOriginal()
    {
        foreach (var item in _iterableData ?? [])
        {
            IteratedData.Add(item);
            yield return (item, Serialize(item));
        }
    }

    /// <summary>
    ///     Iterate over the iterable data loaded into this iterator while saving the iterated data as is in `IteratedData`
    ///     and returning the serialized data
    /// </summary>
    /// <returns> The iterable enumerable with its items serialized </returns>
    public void ApplyToAll<TData>(IEnumerable<TData>? iterator, Action<TData> methodToApply,
        bool parallel)
    {
        iterator ??= IterateEnumerable().Cast<TData>();
        if (parallel) Parallel.ForEach(iterator, methodToApply);
        else
            foreach (var data in iterator)
                methodToApply.Invoke(data);
    }

    /// <summary>
    /// Returns the data contained under the IteratedData list before it was serialized.
    /// </summary>
    /// <param name="indexToFetch"> The index of the data you wish to fetch </param>
    public Data<object> GetDataBeforeSerialization(int indexToFetch)
        => IteratedData[indexToFetch % IteratedData.Count];

    private Data<object> Serialize(Data<object> item)
    {
        if (_serializer == null)
            return item;

        return new Data<object>
        {
            Body = _serializer.Serialize(item.Body),
            MetaData = item.MetaData
        };
    }
}
