using YamlDotNet.Serialization;

internal static class SweepWorkspace
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .DisableAliases()
        .Build();

    public static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "QaaS.Runner.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find QaaS.Runner.sln from the current sweep project location.");
    }

    public static SupportPaths PrepareSupportPaths(string solutionRoot)
    {
        var artifactsRoot = Path.Combine(solutionRoot, "artifacts");
        var casesDirectory = Path.Combine(artifactsRoot, "cases");
        var reportsDirectory = Path.Combine(artifactsRoot, "reports");
        var supportDirectory = Path.Combine(artifactsRoot, "support");
        var inputDirectory = Path.Combine(supportDirectory, "input");
        var foldersDirectory = Path.Combine(supportDirectory, "folders");

        Directory.CreateDirectory(casesDirectory);
        Directory.CreateDirectory(reportsDirectory);
        Directory.CreateDirectory(supportDirectory);
        Directory.CreateDirectory(inputDirectory);
        Directory.CreateDirectory(foldersDirectory);

        var sampleFilePath = Path.Combine(inputDirectory, "sample.json");
        var certificatePath = Path.Combine(inputDirectory, "certificate.pfx");
        var schemaPath = Path.Combine(inputDirectory, "schema.json");
        var runnerExecuteInnerPath = Path.Combine(inputDirectory, "runner-execute-inner.qaas.yaml");

        File.WriteAllText(sampleFilePath, "{\"hello\":\"world\"}");
        File.WriteAllText(certificatePath, "placeholder");
        File.WriteAllText(schemaPath, "{\"type\":\"object\"}");
        File.WriteAllText(runnerExecuteInnerPath, Serialize(new Dictionary<string, object?>
        {
            ["MetaData"] = new Dictionary<string, object?>
            {
                ["Team"] = "SweepTeam",
                ["System"] = "SweepSystem"
            }
        }));

        return new SupportPaths(
            casesDirectory,
            reportsDirectory,
            supportDirectory,
            inputDirectory,
            foldersDirectory,
            sampleFilePath,
            certificatePath,
            schemaPath,
            runnerExecuteInnerPath,
            "support-data-source",
            "support-session",
            "support-stub");
    }

    public static string Serialize(object? value)
    {
        return Serializer.Serialize(value);
    }

    public static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
