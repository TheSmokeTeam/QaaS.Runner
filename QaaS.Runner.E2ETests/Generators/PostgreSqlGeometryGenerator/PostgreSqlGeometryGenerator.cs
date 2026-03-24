using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using Npgsql;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Generator;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.E2ETests.Generators.PostgreSqlGeometryGenerator;

public sealed class PostgreSqlGeometryGeneratorConfig
{
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    [Required]
    public string TableName { get; set; } = string.Empty;
}

public class PostgreSqlGeometryGenerator : BaseGenerator<PostgreSqlGeometryGeneratorConfig>
{
    public override IEnumerable<Data<object>> Generate(IImmutableList<SessionData> sessionDataList,
        IImmutableList<DataSource> dataSourceList)
    {
        using var connection = new NpgsqlConnection(Configuration.ConnectionString);
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                                   CREATE EXTENSION IF NOT EXISTS postgis;
                                   CREATE TABLE IF NOT EXISTS {Configuration.TableName}
                                   (
                                       id integer NOT NULL,
                                       name text NOT NULL,
                                       shape geometry(Polygon, 4326) NOT NULL,
                                       created_at timestamptz NOT NULL DEFAULT current_timestamp
                                   );
                                   TRUNCATE TABLE {Configuration.TableName};
                                   """;
            command.ExecuteNonQuery();
        }

        yield return new Data<object>
        {
            Body = new PostgreSqlGeometryPayload
            {
                Id = 1,
                Name = "geometry-roundtrip",
                Shape = "SRID=4326;POLYGON((35 31,35 32,36 32,36 31,35 31))"
            }
        };
    }
}
