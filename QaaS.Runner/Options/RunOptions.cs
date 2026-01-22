using System.Diagnostics.CodeAnalysis;
using CommandLine;
using QaaS.Framework.SDK.ExecutionObjects;

namespace QaaS.Runner.Options;

/// <summary>
/// Options object to be mapped for `run` runnable command 
/// </summary>
[Verb("run", HelpText = "Run a qaas test according to the given configurations.")
 , ExcludeFromCodeCoverage]
public record RunOptions : AssertableOptions
{
    /// <inheritdoc />
    public override ExecutionType GetExecutionType() => ExecutionType.Run;
}