using NUnit.Framework;

namespace QaaS.Runner.Infrastructure.Tests;

[TestFixture]
public class ConstantsTests
{
    [Test]
    public void ConfigurationSectionNames_ExposeCanonicalRunnerOrder()
    {
        Assert.That(Constants.ConfigurationSectionNames,
            Is.EqualTo(new[]
            {
                "Storages",
                "DataSources",
                "Sessions",
                "Assertions",
                "Links",
                "MetaData"
            }));
    }
}
