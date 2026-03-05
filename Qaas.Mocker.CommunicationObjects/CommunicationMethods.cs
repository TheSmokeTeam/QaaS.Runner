namespace Qaas.Mocker.CommunicationObjects;

/// <summary>
/// Provides methods for creating communication channels and consumer endpoints.
/// </summary>
public static class CommunicationMethods
{
    /// <summary>
    /// The section name for channels from the runner to the mocker.
    /// </summary>
    private const string RunnerToMockerChannelSection = "runner-to-mocker";

    /// <summary>
    /// The section name for channels from the mocker to the runner.
    /// </summary>
    private const string MockerToRunnerChannelSection = "mocker-to-runner";

    /// <summary>
    /// Creates a channel name for communication from the runner to the mocker.
    /// </summary>
    /// <param name="contentType">The type of content being communicated.</param>
    /// <param name="serverName">The name of the server (optional).</param>
    /// <param name="serverInstanceName">The name of the server instance (optional).</param>
    /// <returns>A string representing the channel name.</returns>
    public static string CreateChannelRunnerToMocker(string contentType,
        string? serverName = null, string? serverInstanceName = null)
    {
        var channel = $"{RunnerToMockerChannelSection.ToLower()}:{contentType.ToLower()}";
        if (serverName != null) channel += $":{serverName.ToLower()}";
        if (serverInstanceName != null) channel += $":{serverInstanceName.ToLower()}";
        return channel;
    }

    /// <summary>
    /// Creates a channel name for communication from the mocker to the runner.
    /// </summary>
    /// <param name="contentType">The type of content being communicated.</param>
    /// <param name="serverName">The name of the server (optional).</param>
    /// <param name="serverInstanceName">The name of the server instance (optional).</param>
    /// <returns>A string representing the channel name.</returns>
    public static string CreateChannelMockerToRunner(string contentType,
        string? serverName = null, string? serverInstanceName = null)
    {
        var channel = $"{MockerToRunnerChannelSection.ToLower()}:{contentType.ToLower()}";
        if (serverName != null) channel += $":{serverName.ToLower()}";
        if (serverInstanceName != null) channel += $":{serverInstanceName.ToLower()}";
        return channel;
    }

    /// <summary>
    /// Creates a consumer endpoint name for input.
    /// </summary>
    /// <param name="serverName">The name of the server.</param>
    /// <returns>A string representing the consumer endpoint name.</returns>
    public static string CreateConsumerEndpointInput(string serverName) => $"{serverName.ToLower()}:input";

    /// <summary>
    /// Creates a consumer endpoint name for output.
    /// </summary>
    /// <param name="serverName">The name of the server.</param>
    /// <returns>A string representing the consumer endpoint name.</returns>
    public static string CreateConsumerEndpointOutput(string serverName) => $"{serverName.ToLower()}:output";
}