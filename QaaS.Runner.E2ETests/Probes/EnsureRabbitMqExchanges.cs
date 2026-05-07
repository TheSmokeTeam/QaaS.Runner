using System.Collections.Immutable;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using QaaS.Framework.Protocols.ConfigurationObjects.RabbitMq;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Probe;
using QaaS.Framework.SDK.Session.SessionDataObjects;
using RabbitMQ.Client;

namespace QaaS.Runner.E2ETests.Probes;

public sealed record EnsureRabbitMqExchangesConfig : BaseRabbitMqConfig
{
    [DefaultValue("/")]
    public string RoutingKey { get; set; } = "/";

    [Required, MinLength(1)]
    public RabbitMqExchangeDefinition[]? Exchanges { get; set; }
}

public sealed class RabbitMqExchangeDefinition
{
    [Required]
    public string? Name { get; set; }

    [DefaultValue("direct")]
    public string Type { get; set; } = "direct";

    [DefaultValue(false)]
    public bool Durable { get; set; }

    [DefaultValue(false)]
    public bool AutoDelete { get; set; }
}

public sealed class EnsureRabbitMqExchanges : BaseProbe<EnsureRabbitMqExchangesConfig>
{
    public override void Run(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
    {
        var factory = new ConnectionFactory
        {
            HostName = Configuration.Host!,
            Port = Configuration.Port,
            UserName = Configuration.Username,
            Password = Configuration.Password,
            VirtualHost = Configuration.VirtualHost,
            ContinuationTimeout = TimeSpan.FromSeconds(Configuration.ContinuationTimeoutSeconds),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(Configuration.RequestedConnectionTimeoutSeconds),
            HandshakeContinuationTimeout = TimeSpan.FromSeconds(Configuration.HandshakeContinuationTimeoutSeconds)
        };

        using var connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
        using var channel = connection.CreateChannelAsync().GetAwaiter().GetResult();
        foreach (var exchange in Configuration.Exchanges!)
        {
            channel.ExchangeDeclareAsync(exchange.Name!, exchange.Type.ToLowerInvariant(), exchange.Durable,
                    exchange.AutoDelete)
                .GetAwaiter()
                .GetResult();
            Context.Logger.LogInformation("Ensured RabbitMQ exchange {ExchangeName} of type {ExchangeType}",
                exchange.Name, exchange.Type);
        }
    }
}
