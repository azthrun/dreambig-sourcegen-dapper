using System;
using System.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DreamBig.SourceGen.Dapper.PostgreSql;

/// <summary>
/// Dependency injection extensions for PostgreSQL Dapper repositories.
/// </summary>
public static class DreamBigDapperPostgreSqlServiceCollectionExtensions
{
    /// <summary>
    /// Adds PostgreSQL Dapper support using configuration binding.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration root.</param>
    /// <returns>Service collection.</returns>
    public static IServiceCollection AddDreamBigDapperPostgreSql(
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
            .AddOptions<DreamBigDapperPostgreSqlOptions>()
            .Bind(configuration.GetSection(DreamBigDapperPostgreSqlOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(static options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                "Connection string cannot be null or whitespace.");

        return services.AddDreamBigDapperPostgreSql(static provider =>
            provider.GetRequiredService<IOptions<DreamBigDapperPostgreSqlOptions>>().Value.ConnectionString);
    }

    /// <summary>
    /// Adds PostgreSQL Dapper support using a connection string.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <returns>Service collection.</returns>
    public static IServiceCollection AddDreamBigDapperPostgreSql(
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

        return services.AddDreamBigDapperPostgreSql(_ => connectionString);
    }

    /// <summary>
    /// Adds PostgreSQL Dapper support using a connection string factory.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionStringFactory">Factory for resolving the connection string.</param>
    /// <returns>Service collection.</returns>
    public static IServiceCollection AddDreamBigDapperPostgreSql(
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

        services.AddSingleton(connectionStringFactory);
        services.AddScoped<IDbConnection>(static provider =>
        {
            var resolved = ResolveConnectionString(provider);
            return new NpgsqlConnection(resolved);
        });
        services.AddSingleton<Func<IDbConnection>>(static provider =>
            () => new NpgsqlConnection(ResolveConnectionString(provider)));

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
