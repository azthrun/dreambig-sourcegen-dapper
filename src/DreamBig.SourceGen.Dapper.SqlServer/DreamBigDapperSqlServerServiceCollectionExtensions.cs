using System;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DreamBig.SourceGen.Dapper.SqlServer;

/// <summary>
/// Dependency injection extensions for SQL Server Dapper repositories.
/// </summary>
public static class DreamBigDapperSqlServerServiceCollectionExtensions
{
    /// <summary>
    /// Adds SQL Server Dapper support using configuration binding.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration root.</param>
    /// <returns>Service collection.</returns>
    public static IServiceCollection AddDreamBigDapperSqlServer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        services
            .AddOptions<DreamBigDapperSqlServerOptions>()
            .Bind(configuration.GetSection(DreamBigDapperSqlServerOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(static options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                "Connection string cannot be null or whitespace.");

        return services.AddDreamBigDapperSqlServer(static provider =>
            provider.GetRequiredService<IOptions<DreamBigDapperSqlServerOptions>>().Value.ConnectionString);
    }

    /// <summary>
    /// Adds SQL Server Dapper support using a connection string.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <returns>Service collection.</returns>
    public static IServiceCollection AddDreamBigDapperSqlServer(
        this IServiceCollection services,
        string connectionString)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or whitespace.", nameof(connectionString));
        }

        return services.AddDreamBigDapperSqlServer(_ => connectionString);
    }

    /// <summary>
    /// Adds SQL Server Dapper support using a connection string factory.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionStringFactory">Factory for resolving the connection string.</param>
    /// <returns>Service collection.</returns>
    public static IServiceCollection AddDreamBigDapperSqlServer(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionStringFactory)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (connectionStringFactory is null)
        {
            throw new ArgumentNullException(nameof(connectionStringFactory));
        }

        services.AddSingleton<Func<IServiceProvider, string>>(connectionStringFactory);
        services.AddScoped<IDbConnection>(static provider =>
        {
            var resolved = ResolveConnectionString(provider);
            return new SqlConnection(resolved);
        });
        services.AddSingleton<Func<IDbConnection>>(static provider =>
            () => new SqlConnection(ResolveConnectionString(provider)));

        return services;
    }

    private static string ResolveConnectionString(IServiceProvider services)
    {
        var factory = services.GetRequiredService<Func<IServiceProvider, string>>();
        var connectionString = factory(services);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The connection string factory returned null or whitespace.");
        }

        return connectionString;
    }
}
