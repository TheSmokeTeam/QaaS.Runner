using System.Collections.Immutable;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Generator;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.MetaDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.E2ETests.Generators.TestGenerator;

public class TestGenerator : BaseGenerator<TestGeneratorConfig>
{
    public override IEnumerable<Data<object>> Generate(IImmutableList<SessionData> sessionDataList,
        IImmutableList<DataSource> dataSourceList)
    {
        if (Configuration.Count.HasValue)
        {
            for (var i = 0; i < Configuration.Count; i++)
            {
                yield return new Data<object>
                {
                    Body = new MockJson
                    {
                        Property = "SomeValue"
                    },
                    MetaData = new MetaData
                    {
                        RabbitMq = new RabbitMq
                        {
                            RoutingKey = "SomeRoutingKey"
                        }
                    }
                };
            }
        }
        else
        {
            while (true)
                yield return new Data<object>
                {
                    Body = new MockJson
                    {
                        Property = "SomeValue"
                    },
                    MetaData = new MetaData
                    {
                        RabbitMq = new RabbitMq
                        {
                            RoutingKey = "SomeRoutingKey"
                        }
                    }
                };
        }
    }
}