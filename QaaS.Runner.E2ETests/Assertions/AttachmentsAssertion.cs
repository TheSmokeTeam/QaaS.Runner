using System.Collections.Immutable;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Assertion;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.Serialization;

namespace QaaS.Runner.E2ETests.Assertions;

public class AttachmentsAssertion : BaseAssertion<object>
{
    public override bool Assert(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        for (var item = 0; item < 50; item++)
            AssertionAttachments.Add(new AssertionAttachment
            {
                Data = item,
                Path = Path.Join("try", $"{item.ToString()}.bin"),
                SerializationType = SerializationType.XmlElement
            });

        return true;
    }
}