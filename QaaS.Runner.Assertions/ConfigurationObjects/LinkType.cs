namespace QaaS.Runner.Assertions.ConfigurationObjects;

/// <summary>
///     The type of link to attach to the test results
/// </summary>
public enum LinkType
{
    /// <summary>
    ///     The kibana used to store the tested system's logs during the test
    /// </summary>
    Kibana,

    /// <summary>
    ///     The prometheus used to store the tested system's metrics during the test
    /// </summary>
    Prometheus,

    /// <summary>
    ///     A grafana that displays metrics about the tested system during the test
    /// </summary>
    Grafana
}