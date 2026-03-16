using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Runner.Sessions.RuntimeOverrides;

namespace QaaS.Runner.Sessions.Tests.RuntimeOverrides;

[TestFixture]
public class SessionActionOverrideExtensionsTests
{
    [Test]
    public void SetSessionActionOverrides_When_Global_Dictionary_Is_Null_Initializes_It()
    {
        var context = new InternalContext();
        var overrides = new SessionActionOverrides();

        context.SetSessionActionOverrides(overrides);

        Assert.That(context.InternalGlobalDict, Is.Not.Null);
        Assert.That(context.GetSessionActionOverrides(), Is.SameAs(overrides));
    }

    [Test]
    public void GetSessionActionOverrides_When_Stored_Value_Has_Wrong_Type_Returns_Null()
    {
        var context = new InternalContext
        {
            InternalGlobalDict = new Dictionary<string, object?>
            {
                ["QaaS.Runner.Sessions.SessionActionOverrides"] = new object()
            }
        };

        Assert.That(context.GetSessionActionOverrides(), Is.Null);
    }

    [Test]
    public void GetSessionActionOverrides_When_Global_Dictionary_Is_Null_Returns_Null()
    {
        var context = new InternalContext();

        Assert.That(context.GetSessionActionOverrides(), Is.Null);
    }
}
