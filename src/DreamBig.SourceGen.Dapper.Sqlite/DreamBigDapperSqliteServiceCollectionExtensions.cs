using System;
using System.Data;
using System.Linq;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DreamBig.SourceGen.Dapper.Sqlite;

/// <summary>
/// Dependency injection extensions for SQLite Dapper repositories.
/// </summary>
public static class DreamBigDapperSqliteServiceCollectionExtensions
{
    /// <summary>
    /// Adds SQLite Dapper support using configuration binding.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration root.</param>
    /// <returns>Service collection.</returns>
    public static IServiceCollection AddDreamBigDapperSqlite(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<DreamBigDapperSqliteOptions>()
            .Bind(configuration.GetSection(DreamBigDapperSqliteOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(static options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                "Connection string cannot be null or whitespace.");

        return services.AddDreamBigDapperSqlite(static provider =>
            provider.GetRequiredService<IOptions<DreamBigDapperSqliteOptions>>().Value.ConnectionString);
    }

    /// <summary>
    /// Adds SQLite Dapper support using a connection string.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionString">SQLite connection string.</param>
    /// <returns>Service collection.</returns>
    public static IServiceCollection AddDreamBigDapperSqlite(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or whitespace.", nameof(connectionString));
        }

        return services.AddDreamBigDapperSqlite(_ => connectionString);
    }

    /// <summary>
    /// Adds SQLite Dapper support using a connection string factory.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionStringFactory">Factory for resolving the connection string.</param>
    /// <returns>Service collection.</returns>
    public static IServiceCollection AddDreamBigDapperSqlite(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionStringFactory)
    {
        ArgumentNullException.ThrowIfNull(services);

        ArgumentNullException.ThrowIfNull(connectionStringFactory);

        services.AddSingleton(new SqliteConnectionStringResolver(connectionStringFactory));
        services.AddScoped<IDbConnection>(static provider =>
        {
            var resolved = ResolveConnectionString(provider);
            return new SqliteConnection(resolved);
        });
        services.AddSingleton<Func<IDbConnection>>(static provider =>
            () => new SqliteConnection(ResolveConnectionString(provider)));

        TryAddGeneratedRepositories(services);

        return services;
    }

    private static string ResolveConnectionString(IServiceProvider services)
    {
        var resolver = services.GetRequiredService<SqliteConnectionStringResolver>();
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
/// Provider-scoped holder for the SQLite connection string factory. Registering the raw
/// <see cref="Func{IServiceProvider, String}"/> delegate type would collide with any other
/// library (including the other providers) registering the same delegate type.
/// </summary>
internal sealed class SqliteConnectionStringResolver(Func<IServiceProvider, string> factory)
{
    /// <summary>
    /// Gets the connection string factory.
    /// </summary>
    public Func<IServiceProvider, string> Factory { get; } = factory;
}
