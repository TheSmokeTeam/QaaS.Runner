using System.Diagnostics.CodeAnalysis;
using CommandLine;
using QaaS.Framework.SDK.ExecutionObjects;

namespace QaaS.Runner.Options;

/// <summary>
/// Options object to be mapped for `template` runnable command 
/// </summary>
[Verb("template", HelpText = "Template a qaas configuration file to see how it looks after being loaded, " +
                             "will template what it can even if the configuration file is invalid.")]
public record TemplateOptions : BaseOptions
{
    /// <inheritdoc />
    public override ExecutionType GetExecutionType()
    {
        return ExecutionType.Template;
    }
}
