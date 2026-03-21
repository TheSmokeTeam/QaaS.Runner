using NUnit.Framework;

namespace QaaS.Runner.Tests;

[TestFixture]
public class RunnerConstantsTests
{
    [Test]
    public void SupportedReferenceLists_ContainsExpectedTopLevelListsInOrder()
    {
        Assert.That(Constants.SupportedReferenceLists,
            Is.EqualTo(new[]
            {
                Constants.DataSources,
                Constants.Sessions,
                Constants.Storages,
                Constants.Assertions,
                Constants.Links
            }));
    }

    [Test]
    public void SupportedUniqueIdsPathRegexes_ContainExpectedPatterns()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Constants.SupportedUniqueIdsPathRegexes, Has.Some.Contains("DataSources:\\d+:Name"));
            Assert.That(Constants.SupportedUniqueIdsPathRegexes,
                Has.Some.Contains("Sessions:\\d+:Publishers:\\d+:DataSourceNames:\\d+"));
            Assert.That(Constants.SupportedUniqueIdsPathRegexes,
                Has.Some.Contains("Assertions:\\d+:DataSourceNames:\\d+"));
        });
    }
}
