using System.Collections.Immutable;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Extensions;
using QaaS.Framework.SDK.Hooks.Generator;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;


namespace QaaS.Runner.E2ETests.Generators.FromConsumerGenerator;

public class FromConsumerGenerator : BaseGenerator<FromConsumerGeneratorConfig>
{
    public override IEnumerable<Data<object>> Generate(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        return Context.CurrentRunningSessions.GetSessionByName(Configuration.SessionName)
            .GetOutputByName(Configuration.ConsumerName).GetData();
    }
}