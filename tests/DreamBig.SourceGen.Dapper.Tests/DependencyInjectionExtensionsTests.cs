using System.Collections.Generic;
using System.Data;
using DreamBig.SourceGen.Dapper.PostgreSql;
using DreamBig.SourceGen.Dapper.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Xunit;

namespace DreamBig.SourceGen.Dapper.Tests;

public sealed class DependencyInjectionExtensionsTests
{
    [Fact]
    public void AddDreamBigDapperSqlServer_ShouldRegisterConnections()
    {
        var services = new ServiceCollection();
        services.AddDreamBigDapperSqlServer("Server=.;Database=DreamBig;Trusted_Connection=True;");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        using var connection = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        connection.ShouldBeOfType<SqlConnection>();
        connection.ConnectionString.ShouldBe("Server=.;Database=DreamBig;Trusted_Connection=True;");

        var factory = provider.GetRequiredService<Func<IDbConnection>>();
        using var factoryConnection = factory();
        factoryConnection.ShouldBeOfType<SqlConnection>();
    }

    [Fact]
    public void AddDreamBigDapperPostgreSql_ShouldBindConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DreamBig:Dapper:PostgreSql:ConnectionString"] = "Host=localhost;Database=DreamBig;Username=postgres;Password=postgres",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddDreamBigDapperPostgreSql(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        using var connection = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        connection.ShouldBeOfType<NpgsqlConnection>();
        connection.ConnectionString.ShouldBe("Host=localhost;Database=DreamBig;Username=postgres;Password=postgres");
    }
}
