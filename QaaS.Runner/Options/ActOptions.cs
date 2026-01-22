using System.Diagnostics.CodeAnalysis;
using CommandLine;
using QaaS.Framework.SDK.ExecutionObjects;

namespace QaaS.Runner.Options;

/// <summary>
/// Options object to be mapped for `act` runnable command 
/// </summary>
[Verb("act", HelpText = "Run a qaas test without the assertions and save all sessionData."),
 ExcludeFromCodeCoverage]
public record ActOptions : BaseOptions
{
    /// <inheritdoc />
    public override ExecutionType GetExecutionType()
    {
        return ExecutionType.Act;
    }
}