using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects.RunningSessionsObjects;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.Extensions;
using QaaS.Runner.Sessions.Session.Builders;

namespace QaaS.Runner.Tests.ExtensionsTests;

public class ConfigurationExtensionsTests
{
    private static AssertionBuilder[] GetAssertionConfig(int numberOfAssertions)
    {
        var allAssertionsConfig = new List<AssertionBuilder>();
        for (var i = 1; i <= numberOfAssertions; i++)
        {
            if (i == 3)
            {
                allAssertionsConfig.Add(new AssertionBuilder
                {
                    Assertion = "test",
                    Name = "3",
                    Category = "c1"
                });
                continue;
            }

            if (i == 5)
            {
                allAssertionsConfig.Add(new AssertionBuilder
                {
                    Assertion = "test",
                    Name = "5",
                    Category = null
                });
                continue;
            }

            allAssertionsConfig.Add(new AssertionBuilder
            {
                Assertion = "test",
                Name = i.ToString(),
                Category = "c" + i
            });
        }

        return allAssertionsConfig.ToArray();
    }

    private static IEnumerable<TestCaseData> FilterAssertionsInput()
    {
        var config = GetAssertionConfig(5);
        yield return new TestCaseData(
            config,
            new[] { "2" },
            new[] { "c1" },
            new[] { "1", "2", "3" } // session 3 is also category c1
        ).SetName("both filters");

        config = GetAssertionConfig(5);
        yield return new TestCaseData(
            config,
            null,
            new[] { "c1" },
            new[] { "1", "3" } // assertion 3 is also category c1
        ).SetName("only categories");

        config = GetAssertionConfig(5);
        yield return new TestCaseData(
            config,
            new[] { "1" },
            null,
            new[] { "1" }
        ).SetName("only name");
    }

    [Test]
    [TestCaseSource(nameof(FilterAssertionsInput))]
    public void
        TestFilterConfigurationByAssertionNames_CallFunctionToFilterSpecificAssertions_ShouldFilterAssertionsGiven(
            AssertionBuilder[] config,
            string[]? assertionNamesToRun, string[]? assertionCategoriesToRun,
            string[] expectedFilteredAssertionNames)
    {
        var context = new InternalContext
        {
            Logger = Globals.Logger, RootConfiguration = new ConfigurationBuilder().Build(),
            InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>())
        };

        // Act
        config = config.FilterConfigurationByAssertion(assertionNamesToRun, assertionCategoriesToRun, context);

        // Assert
        var filteredAssertionNames = config.Select(assertion => assertion.Name!).ToList();
        filteredAssertionNames.Sort();
        Array.Sort(expectedFilteredAssertionNames);
        Assert.That(filteredAssertionNames, Is.EqualTo(expectedFilteredAssertionNames));
    }

    private static SessionBuilder[] CreateSessionConfiguration(int numberOfSessions)
    {
        var allSessionsConfig = new List<SessionBuilder>();
        for (var i = 1; i <= numberOfSessions; i++)
        {
            if (i == 3)
            {
                allSessionsConfig.Add(new SessionBuilder
                {
                    Name = "3",
                    Category = "c1"
                });
                continue;
            }

            if (i == 5)
            {
                allSessionsConfig.Add(new SessionBuilder
                {
                    Name = "5",
                    Category = null
                });
                continue;
            }

            allSessionsConfig.Add(new SessionBuilder
            {
                Name = i.ToString(),
                Category = "c" + i
            });
        }

        return allSessionsConfig.ToArray();
    }

    private static IEnumerable<TestCaseData> FilterAssertionsAndSessions()
    {
        var sessionsConfig = CreateSessionConfiguration(70);
        var assertionConfig = GetAssertionConfig(5);

        assertionConfig[0].SessionNames = new[] { "4" };
        assertionConfig[2].SessionNames = new[] { "9" };
        assertionConfig[1].SessionNamePatterns = new[] { "^50.*", "^7.*" };

        yield return new TestCaseData(
            sessionsConfig,
            assertionConfig,
            null,
            null,
            null,
            new[] { "c1" },
            new[] { "1", "3" } // session 3 is also category c1
        ).SetName("only session categories");

        sessionsConfig = CreateSessionConfiguration(70);
        yield return new TestCaseData(
            sessionsConfig,
            assertionConfig,
            null,
            null,
            new[] { "1", "2", "3" },
            null,
            new[] { "1", "2", "3" }
        ).SetName("only session names");

        sessionsConfig = CreateSessionConfiguration(70);
        yield return new TestCaseData(
            sessionsConfig,
            assertionConfig,
            new[] { "2" },
            new[] { "c1" },
            null,
            null,
            new[] { "4", "7", "9", "50", "70" }
        ).SetName("every filter but session categories");

        sessionsConfig = CreateSessionConfiguration(70);
        yield return new TestCaseData(
            sessionsConfig,
            assertionConfig,
            new[] { "2" },
            new[] { "c1" },
            new[] { "6" },
            new[] { "c8" },
            new[] { "4", "6", "7", "8", "9", "50", "70" }
        ).SetName("every filter");

        sessionsConfig = CreateSessionConfiguration(70);
        yield return new TestCaseData(
            sessionsConfig,
            Array.Empty<AssertionBuilder>(),
            new[] { "6" }, // assertion names filter
            null, // assertion categories filter
            new[] { "71" }, // session names filter
            null, // session categories filter
            Array.Empty<string>() // expected filtered names
        ).SetName("no values to filter exception");

        sessionsConfig = CreateSessionConfiguration(70);
        yield return new TestCaseData(
            sessionsConfig,
            assertionConfig,
            new[] { "6" }, // assertion names filter
            null, // assertion categories filter
            new[] { "71" }, // session names filter
            null, // session categories filter
            Array.Empty<string>() // expected filtered names
        ).SetName("no existing names exception");
    }

    [Test]
    [TestCaseSource(nameof(FilterAssertionsAndSessions))]
    public void
        TestFilterConfigurationBySessionNamesAndAssertionNames_CallFunctionToFilterSpecificSessions_ShouldFilterSessionsGiven(
            SessionBuilder[] sessionConfig,
            AssertionBuilder[] assertionConfig,
            string[]? assertionNamesToRun, string[]? assertionCategoriesToRun,
            string[]? sessionNamesToRun, string[]? sessionCategoriesToRun,
            string[] expectedFilteredSessionNames)
    {
        var context = new InternalContext
        {
            Logger = Globals.Logger, RootConfiguration = new ConfigurationBuilder().Build(),
            InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>())
        };
        // Act
        if (!expectedFilteredSessionNames.Any())
            Assert.Throws<InvalidOperationException>(() => sessionConfig.FilterConfigurationBySessionsAndAssertions(
                assertionConfig,
                sessionNamesToRun,
                assertionNamesToRun,
                sessionCategoriesToRun,
                assertionCategoriesToRun,
                context));
        else
        {
            sessionConfig =
                sessionConfig.FilterConfigurationBySessionsAndAssertions(
                    assertionConfig,
                    sessionNamesToRun,
                    assertionNamesToRun,
                    sessionCategoriesToRun,
                    assertionCategoriesToRun,
                    context);

            // Assert
            var filteredSessionNames = sessionConfig.Select(session => session.Name).ToList();
            filteredSessionNames.Sort();
            Array.Sort(expectedFilteredSessionNames);
            Assert.That(filteredSessionNames, Is.EqualTo(expectedFilteredSessionNames));
        }
    }

    [Test]
    public void
        TestFilterConfigurationByAssertions_CallFunctionWithNoFilters_ReturnAllAssertions()
    {
        var context = new InternalContext
        {
            Logger = Globals.Logger, RootConfiguration = new ConfigurationBuilder().Build(),
            InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>())
        };
        var assertionConfig = GetAssertionConfig(5);

        assertionConfig = assertionConfig.FilterConfigurationByAssertion(
            null,
            null,
            context);

        var filteredAssertionNames = assertionConfig.Select(assertion => assertion.Name).ToList();
        filteredAssertionNames.Sort();
        Assert.That(filteredAssertionNames, Is.EqualTo(new List<string> { "1", "2", "3", "4", "5" }));
    }

    [Test]
    public void TestFilterConfigurationBySessionsAndAssertions_CallFunctionWithNoFilters_ReturnAllSessions()
    {
        var context = new InternalContext
        {
            Logger = Globals.Logger, RootConfiguration = new ConfigurationBuilder().Build(),
            InternalRunningSessions = new RunningSessions(new Dictionary<string, RunningSessionData<object, object>>())
        };
        var sessionsConfig = CreateSessionConfiguration(5);
        var assertionConfig = GetAssertionConfig(5);

        sessionsConfig = sessionsConfig.FilterConfigurationBySessionsAndAssertions(
            assertionConfig,
            null,
            null,
            null,
            null,
            context);

        var filteredSessionNames = sessionsConfig.Select(session => session.Name).ToList();
        filteredSessionNames.Sort();
        Assert.That(filteredSessionNames, Is.EqualTo(new List<string> { "1", "2", "3", "4", "5" }));
    }
}
