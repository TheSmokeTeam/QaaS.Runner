using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Runner.Assertions.ConfigurationObjects;
using QaaS.Runner.Sessions.Session.Builders;

namespace QaaS.Runner.Extensions;

/// <summary>
///     Adds methods that could be used as inner methods to configuration objects.
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    ///     Checks if the user entered a non-existing name under a name / category filter.
    /// </summary>
    /// <param name="existingNames"> The categories/names of sessions/assertions qaas recognises </param>
    /// <param name="filterNames"> The categories/names of sessions/assertions the user asked to filter by </param>
    /// <param name="flagName"> The name of the flag the user entered the filters under </param>
    /// <param name="context"></param>
    /// <exception cref="InvalidOperationException"> If received unknown names/categories - throws an exception  </exception>
    private static void TestForUnknownNames(IList<string> existingNames, IList<string> filterNames, string flagName,
        Context context)
    {
        if (existingNames.Count == 0)
            throw new InvalidOperationException($"No values to filter under the `{flagName}` flag");
        var notFoundNames = filterNames.Except(existingNames).ToList();
        if (notFoundNames.Any())
        {
            context.Logger.LogDebug("Existing names received: {existingNames}",
                string.Join(", ", existingNames.Intersect(filterNames)));
            throw new InvalidOperationException($"Received non-existing names from `{flagName}` flag: " +
                                                string.Join(", ", notFoundNames));
        }
    }

    /// <returns> The same configuration with only the assertions that are a part of the assertionNamesToRun. </returns>
    private static AssertionBuilder[] FilterConfigurationByAssertionNames(
        this AssertionBuilder[] configurations,
        IList<string>? assertionNamesToRun, Context context)
    {
        if (assertionNamesToRun == null) return [];

        var assertionsWithValue = configurations.Where(assertion => assertion.Name != null);
        var assertionsWithValueAsArray = assertionsWithValue as AssertionBuilder[] ?? assertionsWithValue.ToArray();

        var assertionNames = assertionsWithValueAsArray.Select(assertion => assertion.Name).ToList();
        TestForUnknownNames(assertionNames!, assertionNamesToRun, "assertion-names-to-run", context);

        return configurations.Where(assertion => assertionNamesToRun.Contains(assertion.Name!)).ToArray();
    }

    /// <returns> The same configuration with only the assertions that are a part of the categories to run. </returns>
    private static AssertionBuilder[] FilterConfigurationByAssertionCategories(
        this AssertionBuilder[] configurations,
        IList<string>? assertionCategoriesToRun, Context context)
    {
        if (assertionCategoriesToRun == null) return [];

        var assertionsWithValue = configurations.Where(assertion => assertion.Category != null);
        var assertionsWithValueAsArray = assertionsWithValue as AssertionBuilder[] ?? assertionsWithValue.ToArray();

        var assertionCategories = assertionsWithValueAsArray.Select(assertion => assertion.Category).ToList();
        TestForUnknownNames(assertionCategories!, assertionCategoriesToRun, "assertion-categories-to-run", context);

        return configurations.Where(assertion => assertion.Category != null)
            .Where(assertion => assertionCategoriesToRun.Contains(assertion.Category!)).ToArray();
    }

    /// <returns> The same configuration with only the sessions that are a part of the names of the session to run. </returns>
    private static SessionBuilder[] FilterConfigurationBySessionNames(
        this SessionBuilder[] configurations,
        IList<string>? sessionNamesToRun, Context context)
    {
        if (sessionNamesToRun == null) return [];

        var sessionNames = configurations.Select(session => session.Name).ToList();
        TestForUnknownNames(sessionNames!, sessionNamesToRun, "session-names-to-run", context);

        return configurations.Where(session => sessionNamesToRun.Contains(session.Name!)).ToArray();
    }

    /// <returns> The same configuration with only the sessions that are a part of the categories to run. </returns>
    private static SessionBuilder[] FilterConfigurationBySessionCategories(
        this SessionBuilder[] configurations,
        IList<string>? sessionCategoriesToRun, Context context)
    {
        if (sessionCategoriesToRun == null) return [];

        var sessionsWithValue = configurations.Where(session => session.Category != null);
        var sessionsWithValueAsArray = sessionsWithValue as SessionBuilder[] ?? sessionsWithValue.ToArray();

        var sessionCategories = sessionsWithValueAsArray.Select(sessions => sessions.Category).ToList();
        TestForUnknownNames(sessionCategories!, sessionCategoriesToRun, "session-categories-to-run", context);

        return configurations.Where(session => session.Category != null)
            .Where(session => sessionCategoriesToRun.Contains(session.Category!)).ToArray();
    }

    /// <returns>
    ///     returns configuration containing only the assertions that are named under 'assertionNamesToRun'
    ///     or has a category in 'assertionCategoriesToRun'
    /// </returns>
    public static AssertionBuilder[] FilterConfigurationByAssertion(
        this AssertionBuilder[] configurations,
        IList<string>? assertionNamesToRun, IList<string>? assertionCategoriesToRun, Context context)
    {
        if (assertionCategoriesToRun == null && assertionNamesToRun == null) return configurations;
        configurations = FilterConfigurationByAssertionCategories(configurations, assertionCategoriesToRun, context)
            .Union(FilterConfigurationByAssertionNames(configurations, assertionNamesToRun, context)).ToArray();
        return configurations;
    }

    /// <returns>
    ///     returns conf containing only the sessions that are named under 'sessionNamesToRun',
    ///     'sessionCategoriesToRun', or are one of the sessions that an assertion the user wishes to run
    ///     depends on (filtered by assertionNamesToRun and assertionCategoriesToRun).
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     assertionNamesToRun contains non-existing assertion name or
    ///     sessionNamesToRun contains non-existing session name
    /// </exception>
    public static SessionBuilder[] FilterConfigurationBySessionsAndAssertions(
        this SessionBuilder[] sessionConfigurations, AssertionBuilder[] assertionConfiguration,
        IList<string>? sessionNamesToRun, IList<string>? assertionNamesToRun,
        IList<string>? sessionCategoriesToRun, IList<string>? assertionCategoriesToRun, Context context)
    {
        if (sessionCategoriesToRun == null && sessionNamesToRun == null &&
            assertionCategoriesToRun == null && assertionNamesToRun == null) return sessionConfigurations;

        if (assertionCategoriesToRun == null && assertionNamesToRun == null)
        {
            sessionConfigurations =
                FilterConfigurationBySessionNames(sessionConfigurations, sessionNamesToRun, context)
                    .Union(FilterConfigurationBySessionCategories(sessionConfigurations, sessionCategoriesToRun,
                        context)).ToArray();
            return sessionConfigurations;
        }

        var assertionsToRun = FilterConfigurationByAssertion(assertionConfiguration, assertionNamesToRun,
            assertionCategoriesToRun, context);

        var allSessions = sessionConfigurations.Select(session => session.Name).ToList();
        var sessionsInAssertions =
            assertionsToRun.Where(assertion => assertion.SessionNames != null)
                .SelectMany(assertion => assertion.SessionNames!);
        var sessionNamePatternsInAssertions =
            assertionsToRun.Where(assertion => assertion.SessionNamePatterns != null)
                .SelectMany(assertion => assertion.SessionNamePatterns!);
        var sessionNamesInAssertions = allSessions.Where(s =>
            sessionNamePatternsInAssertions.Any(p => Regex.IsMatch(s!, p)) || sessionsInAssertions.Contains(s));

        sessionNamesToRun = sessionNamesToRun == null
            ? sessionNamesInAssertions.ToList()
            : sessionNamesToRun.Union(sessionNamesInAssertions).ToList();

        sessionConfigurations =
            FilterConfigurationBySessionNames(sessionConfigurations, sessionNamesToRun, context)
                .Union(FilterConfigurationBySessionCategories(sessionConfigurations, sessionCategoriesToRun, context))
                .ToArray();

        return sessionConfigurations;
    }
}