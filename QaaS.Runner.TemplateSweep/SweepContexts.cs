internal static class SweepContexts
{
    /// <summary>
    /// Builds a template document that keeps optional fields blank unless the field is required by validation.
    /// </summary>
    public static object? BuildRequiredOnlyDocument(ContextSpec context, HookInventory hooks, SupportPaths support)
    {
        var target = SweepNodes.BuildMinimalNode(context.TargetType, context, support, false);
        SweepNodes.ApplyOverrides(target, context.BaselineOverrides);
        SweepNodes.ApplyOverrides(target, context.RequiredOnlyOverrides);
        SweepNodes.RemovePaths(ref target, context.RequiredOnlyRemovals);
        var document = context.CreateRoot(target, hooks, support);
        InjectGlobalBaselines(document, context, support);
        return document;
    }

    /// <summary>
    /// Builds a maximally populated template document while preserving cross-field validity constraints.
    /// </summary>
    public static object? BuildFilledDocument(ContextSpec context, HookInventory hooks, SupportPaths support)
    {
        var target = SweepNodes.BuildMinimalNode(context.TargetType, context, support, false);
        SweepNodes.ApplyOverrides(target, context.BaselineOverrides);

        var adjustedTarget = SweepNodes.DeepCloneNode(target);
        foreach (var field in SweepNodes.EnumerateFields(context))
        {
            var hasBaselineOverride = context.BaselineOverrides.TryGetValue(field.DisplayPath, out var baselineValue);
            var value = hasBaselineOverride && baselineValue != null
                ? SweepNodes.DeepCloneNode(baselineValue)
                : SweepNodes.CreateExplicitNode(field.OwnerType, field.Property, field.FieldType, support);
            SweepNodes.SetNodeValue(ref adjustedTarget, field.Tokens, value);
        }

        SweepNodes.ApplyOverrides(adjustedTarget, context.BaselineOverrides);
        SweepNodes.ApplyOverrides(adjustedTarget, context.FilledOverrides);
        SweepNodes.RemovePaths(ref adjustedTarget, context.FilledRemovals);

        var document = context.CreateRoot(adjustedTarget, hooks, support);
        InjectGlobalBaselines(document, context, support);
        return document;
    }

    private static void InjectGlobalBaselines(Dictionary<string, object?> document, ContextSpec context, SupportPaths support)
    {
        if (context.Domain == "runner" && !document.ContainsKey("MetaData"))
        {
            document["MetaData"] = new Dictionary<string, object?>
            {
                ["Team"] = "SweepTeam",
                ["System"] = "SweepSystem"
            };
        }
    }
}
