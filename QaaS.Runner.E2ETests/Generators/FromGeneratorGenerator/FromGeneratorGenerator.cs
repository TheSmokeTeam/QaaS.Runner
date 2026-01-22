using System.Collections.Immutable;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Hooks.Generator;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.E2ETests.Generators.FromGeneratorGenerator;

public class FromGeneratorGenerator : BaseGenerator<object>
{
    public override IEnumerable<Data<object>> Generate(IImmutableList<SessionData> sessionDataList,
        IImmutableList<DataSource> dataSourceList)
    {
        var i = 0;
        // assumes that the generator given is TestGenerator
        return dataSourceList.AsSingle().Retrieve()
            .Select(data => new Data<object>
            {
                Body = i++ % 2 == 0 ? data.Body : "OtherValue",
                MetaData = data.MetaData
            });
    }
}