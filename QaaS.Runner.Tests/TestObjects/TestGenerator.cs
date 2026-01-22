using System.Collections.Immutable;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Generator;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.Tests.TestObjects;

public class TestGenerator: BaseGenerator<object>
{
    public override IEnumerable<Data<object>> Generate(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        return [new Data<object>()];
    }
}