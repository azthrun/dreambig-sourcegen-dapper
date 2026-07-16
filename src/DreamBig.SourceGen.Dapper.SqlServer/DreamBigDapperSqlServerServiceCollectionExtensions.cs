using System;
using System.Data;
using System.Linq;
using System.Reflection;
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
        ArgumentNullException.ThrowIfNull(services);

        ArgumentNullException.ThrowIfNull(configuration);

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
        ArgumentNullException.ThrowIfNull(services);

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
        ArgumentNullException.ThrowIfNull(services);

        ArgumentNullException.ThrowIfNull(connectionStringFactory);

        services.AddSingleton(new SqlServerConnectionStringResolver(connectionStringFactory));
        services.AddScoped<IDbConnection>(static provider =>
        {
            var resolved = ResolveConnectionString(provider);
            return new SqlConnection(resolved);
        });
        services.AddSingleton<Func<IDbConnection>>(static provider =>
            () => new SqlConnection(ResolveConnectionString(provider)));

        TryAddGeneratedRepositories(services);

        return services;
    }

    private static string ResolveConnectionString(IServiceProvider services)
    {
        var resolver = services.GetRequiredService<SqlServerConnectionStringResolver>();
        var connectionString = resolver.Factory(services);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The connection string factory returned null or whitespace.");
        }

        return connectionString;
    }

    private static void TryAddGeneratedRepositories(IServiceCollection services)
    {
        // Every consumer assembly emits a registration type with the same full name; invoke all of
        // them so repositories spread across multiple assemblies are all registered. The generated
        // registrations use TryAdd semantics, so repeated invocation is safe.
        var registrationMethods = AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(static assembly =>
            {
                try
                {
                    return assembly.GetType(GeneratedExtensionsTypeName, throwOnError: false);
                }
                catch (ReflectionTypeLoadException)
                {
                    return null;
                }
            })
            .Where(static type => type is not null)
            .Select(static type => type!.GetMethod(
                "AddDreamBigDapperGenerated",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(IServiceCollection)],
                modifiers: null))
            .Where(static method => method is not null);

        foreach (var method in registrationMethods)
        {
            method!.Invoke(null, [services]);
        }
    }

    private const string GeneratedExtensionsTypeName =
        "DreamBig.SourceGen.Dapper.DreamBigDapperGeneratedServiceCollectionExtensions";
}

/// <summary>
/// Provider-scoped holder for the SQL Server connection string factory. Registering the raw
/// <see cref="Func{IServiceProvider, String}"/> delegate type would collide with any other
/// library (including the PostgreSQL provider) registering the same delegate type.
/// </summary>
internal sealed class SqlServerConnectionStringResolver(Func<IServiceProvider, string> factory)
{
    /// <summary>
    /// Gets the connection string factory.
    /// </summary>
    public Func<IServiceProvider, string> Factory { get; } = factory;
}
