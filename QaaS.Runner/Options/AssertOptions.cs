using System.Diagnostics.CodeAnalysis;
using CommandLine;
using QaaS.Framework.SDK.ExecutionObjects;

namespace QaaS.Runner.Options;

/// <summary>
/// Options object to be mapped for `assert` runnable command 
/// </summary>
[Verb("assert", HelpText = "Run a qaas test with only the assertions on already existing sessionData."),
 ExcludeFromCodeCoverage]
public record AssertOptions : AssertableOptions
{
    /// <inheritdoc />
    public override ExecutionType GetExecutionType() => ExecutionType.Assert;
}