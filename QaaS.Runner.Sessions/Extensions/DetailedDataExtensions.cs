using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.MetaDataObjects;

namespace QaaS.Runner.Sessions.Extensions;

public static class DetailedDataExtensions
{
    /// <summary>
    ///     Adds an index to a detailed data object
    /// </summary>
    public static DetailedData<T> AddIoMatchIndexToDetailedData<T>(this DetailedData<T> data, int index)
    {
        return data with
        {
            MetaData = data.MetaData == null
                ? new MetaData { IoMatchIndex = index }
                : data.MetaData with { IoMatchIndex = index }
        };
    }
}