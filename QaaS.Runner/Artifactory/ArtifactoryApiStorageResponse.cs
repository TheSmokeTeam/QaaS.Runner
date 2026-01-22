namespace QaaS.Runner.Artifactory;

/// <summary>
///     The relevant fields in the artifactory's storage api response to this class's functions
/// </summary>
internal class ArtifactoryApiStorageResponse
{
    public IList<ArtifactoryChild>? Children { get; set; }
}

internal class ArtifactoryChild
{
    public string? Uri { get; set; }
}