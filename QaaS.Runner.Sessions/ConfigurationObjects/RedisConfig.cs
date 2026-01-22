using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using StackExchange.Redis;

namespace QaaS.Runner.Sessions.ConfigurationObjects;

[ExcludeFromCodeCoverage]
public record RedisConfig
{
    [Required]
    [Description("Redis hostname (should contain the port too for example: - 'host1:8080')")]
    public string? Host { get; set; }

    [Description("User for the redis server")]
    [DefaultValue(null)]
    public string? Username { get; set; } = null;

    [Description("Password for the redis server")]
    [DefaultValue(null)]
    public string? Password { get; set; } = null;

    [Description("If true, connect will not create connection while no servers are available")]
    [DefaultValue(true)]
    public bool AbortOnConnectFail { get; set; } = true;

    [Description("The number of times to repeat connect attempts during initial connect")]
    [DefaultValue(3)]
    public int ConnectRetry { get; set; } = 3;

    [Description("Identification for the connection within redis")]
    [DefaultValue(null)]
    public string? ClientName { get; set; }

    [Description("Timeout to allow for asynchronous operations")]
    [DefaultValue(5000)]
    public int AsyncTimeoutMs { get; set; } = 5000;

    [Description("Specifies whether SSL encryption should be used")]
    [DefaultValue(false)]
    public bool Ssl { get; set; } = false;

    [Description("Enforces a preticular SSL host identity on the server's certificate")]
    [DefaultValue(null)]
    public string? SslHost { get; set; } = null;

    [Description("Time at which to send a message to help keep alive")]
    [DefaultValue(60)]
    public int KeepAliveSeconds { get; set; } = 60;

    [Description("Redis database to use")]
    [DefaultValue(0)]
    public int RedisDataBase { get; set; } = 0;

    public ConfigurationOptions CreateRedisConfigurationOptions()
    {
        return new ConfigurationOptions
        {
            EndPoints = { Host! },
            User = Username,
            Password = Password,
            AbortOnConnectFail = AbortOnConnectFail,
            ConnectRetry = ConnectRetry,
            KeepAlive = KeepAliveSeconds,
            ClientName = ClientName,
            AsyncTimeout = AsyncTimeoutMs,
            Ssl = Ssl,
            SslHost = SslHost
        };
    }
}