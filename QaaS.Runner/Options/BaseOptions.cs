using System.ComponentModel.DataAnnotations;
using CommandLine;
using QaaS.Framework.Configurations.CustomValidationAttributes;
using QaaS.Framework.Configurations.References;
using QaaS.Framework.Executions.Options;
using QaaS.Framework.SDK.ExecutionObjects;

namespace QaaS.Runner.Options;

/// <summary>
///     Base options for any command that is `runnable` meaning it runs something according to the qaas configuration file
/// </summary>
public abstract record BaseOptions : LoggerOptions
{
    [Required]
    [ValidPath]
    [Value(0, Default = Constants.DefaultQaasConfigurationFileName,
        HelpText = "Path to a qaas yaml configuration file to use with the command.")]
    public string? ConfigurationFile { get; init; }

    [AllPathsInEnumerableValid]
    [Option('w', "with-files", Default = null,
        HelpText =
            "List of files to overwrite the qaas configuration with, The first file overwrites the qaas configuration file and then the one after it overwrite the result and so on...")]
    public IList<string> OverwriteFiles { get; init; }

    [Option('r', "overwrite-arguments", Default = null,
        HelpText = @"
List of arguments to overwrite the qaas configuration with, The first argument overwrites the qaas configuration and then the one after it overwrites the result and so on...
For example: `Path:To:Variable:To:Overwrite=NewVariableValue`
")]
    public IList<string> OverwriteArguments { get; init; }

    [Option('p', "push-references", Default = null,
        HelpText = @"
References to push onto the qaas configuration.
References are configurations that are pushed in the completed test case's root level list configurations instead of a certain keyword to replace,
if such a keyword is not found for a certain list nothing will be added to it.
The items added to the configuration list will have a prefix of the given keyword to replace added to their unique name field.

For example:
If we push the reference below
`reference.yaml`
```
Sessions:
  - Name: A
  - Name: B
```
On the configuration below 
`test.qaas.yaml`
```
Sessions:
  - Name: First
  - ReplaceKeyWord
  - Name: Seconds
```
With the replace keyword `ReplaceKeyWord` we will get the following results:
```
Sessions:
  - Name: First
  - Name: ReplaceKeyWordA
  - Name: ReplaceKeyWordB
  - Name: Seconds
```

The syntax to add a reference is:
`-p KeyWordToReplace Path/To/Reference.yaml PathToReferenceOverridingFiles.yaml`

To add multiple references you can invoke the -p flag multiple times or
 use a single -p flag but have the references seperated by the KeyWordToReplace:
`-p KeyWordToReplace1 Reference1.yaml -p KeyWordToReplace2 Reference2.yaml`
Or
`-p KeyWordToReplace1 Reference1.yaml KeyWordToReplace2 Reference2.yaml`

!!! Note that the `KeyWordToReplace` must not end with the suffix `.yml` or `.yaml`.
")]
    public IList<string> PushReferences { get; init; } = Array.Empty<string>();

    [ValidPath]
    [Option('c', "cases", Default = null,
        HelpText = "Name of the folder containing all the different .yaml case files that cases are ran with, " +
                   "If no folder is given will run the .qaas.yaml file as is without cases.")]
    public string? CasesRootDirectory { get; init; }

    [Option('n', "cases-names", Default = null,
        HelpText = "Names of the test-cases to run.")]
    public IList<string> CasesNamesToRun { get; init; } = Array.Empty<string>();

    [Option('i', "session-names", Default = null,
        HelpText = "Names of the sessions to run.")]
    public IList<string> SessionNamesToRun { get; init; } = Array.Empty<string>();

    [Option("session-categories", Default = null,
        HelpText = "Used to filter the session categories to run.")]
    public IList<string> SessionCategoriesToRun { get; init; } = Array.Empty<string>();

    [Option('a', "assertion-names", Default = null,
        HelpText = "Names of the assertions to run. Only sessions given to those assertions will run.")]
    public IList<string> AssertionNamesToRun { get; init; } = Array.Empty<string>();

    [Option("assertion-categories", Default = null,
        HelpText =
            "Used to filter the assertion categories to run. The sessions that are associated with assertions will run too.")]
    public IList<string> AssertionCategoriesToRun { get; init; } = Array.Empty<string>();

    [Option("resolve-cases-last", Default = false,
        HelpText = "When this flag is used cases will be resolved after all other types of configuration" +
                   " resolutions, instead of its default behaviour which is after overwrite files and before references.")]
    public bool ResolveCasesLast { get; init; } = false;

    [Option("no-env", Default = false,
        HelpText = "When this flag is used environment variables will not override loaded configurations.")]
    public bool DontResolveWithEnvironmentVariables { get; init; } = false;

    /// <summary>
    ///     Gets the execution type of the command
    /// </summary>
    public abstract ExecutionType GetExecutionType();

    /// <summary>
    ///     Parses the PushReferences property as a list of ReferenceConfigs
    /// </summary>
    public IEnumerable<ReferenceConfig> GetParsedPushReferences()
    {
        var currentReferenceFiles = new List<string>();
        var currentReferenceReplaceKeyword = PushReferences.FirstOrDefault();
        if (currentReferenceReplaceKeyword == null) yield break;
        if (IsStringPathToYamlFile(currentReferenceReplaceKeyword))
            throw new ArgumentException("The first argument in push-references must be a keyword" +
                                        " and not a path to a yaml file.");
        foreach (var item in PushReferences.Skip(1))
            if (IsStringPathToYamlFile(item))
            {
                currentReferenceFiles.Add(item);
            }
            else
            {
                if (currentReferenceFiles.Count <= 0)
                    throw new ArgumentException($"No yaml files were given for the current reference" +
                                                $" replace keyword `{currentReferenceReplaceKeyword}`.");
                yield return new ReferenceConfig
                {
                    ReferenceReplaceKeyword = currentReferenceReplaceKeyword,
                    ReferenceFilesPaths = currentReferenceFiles
                };
                currentReferenceReplaceKeyword = item;
                currentReferenceFiles = new List<string>();
            }

        if (currentReferenceFiles.Count <= 0)
            throw new ArgumentException($"No yaml files were given for the current reference" +
                                        $" replace keyword `{currentReferenceReplaceKeyword}`.");
        yield return new ReferenceConfig
        {
            ReferenceReplaceKeyword = currentReferenceReplaceKeyword,
            ReferenceFilesPaths = currentReferenceFiles
        };
    }

    private static bool IsStringPathToYamlFile(string str)
    {
        return str.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
               || str.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);
    }
}