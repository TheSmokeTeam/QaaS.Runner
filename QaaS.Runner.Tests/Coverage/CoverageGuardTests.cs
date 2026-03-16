using System.Diagnostics.CodeAnalysis;
using NUnit.Framework;

namespace QaaS.Runner.Tests.Coverage;

[TestFixture]
public class CoverageGuardTests
{
    [Test]
    public void ExcludeFromCodeCoverage_ShouldOnlyBeUsedOnConfigurationTypes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var violatingFiles = Directory.EnumerateFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(file => File.ReadAllText(file).Contains($"[{nameof(ExcludeFromCodeCoverageAttribute)}]",
                StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}ConfigurationObjects{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(file =>
            {
                var fileName = Path.GetFileName(file);
                return !fileName.Contains("Config", StringComparison.OrdinalIgnoreCase)
                       && !fileName.Contains("Configuration", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        Assert.That(violatingFiles, Is.Empty,
            "Only configuration types may opt out of code coverage. Violations: " +
            string.Join(", ", violatingFiles));
    }

    private static string FindRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (currentDirectory != null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "QaaS.Runner.sln")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the QaaS.Runner repository root.");
    }
}
