using System.Data;
using Dapper;
using DreamBig.SourceGen.Dapper.Attributes;
using DreamBig.SourceGen.Dapper.Internal;

namespace DreamBig.SourceGen.Dapper.Extensions;

/// <summary>
/// Runtime helpers used by source-generated repository implementations.
/// </summary>
public static class GeneratedDapperExtensions
{
    /// <summary>
    /// Builds Dapper dynamic parameters for stored procedure execution.
    /// </summary>
    /// <param name="parameters">Tuple containing name, value, metadata.</param>
    /// <returns>Dynamic parameters object.</returns>
    public static DynamicParameters BuildParameters(
        IEnumerable<(string Name, object? Value, DbParamAttribute? Metadata)> parameters)
    {
        var dynamicParameters = new DynamicParameters();

        foreach (var (name, value, metadata) in parameters)
        {
            if (metadata is null)
            {
                dynamicParameters.Add(name, value, direction: ParameterDirection.Input);
                continue;
            }

            var direction = metadata.Direction switch
            {
                DbParamDirection.Output => ParameterDirection.Output,
                DbParamDirection.InputOutput => ParameterDirection.InputOutput,
                _ => ParameterDirection.Input,
            };

            dynamicParameters.Add(
                metadata.Name,
                value,
                metadata.DbType,
                direction,
                metadata.Size);
        }

        return dynamicParameters;
    }

    /// <summary>
    /// Executes a generated query and returns rows.
    /// </summary>
    /// <typeparam name="T">Row type.</typeparam>
    /// <param name="connection">Database connection.</param>
    /// <param name="sql">SQL query.</param>
    /// <param name="param">SQL parameters.</param>
    /// <param name="transaction">Optional transaction.</param>
    /// <param name="commandTimeout">Optional timeout.</param>
    /// <returns>Result rows.</returns>
    public static IEnumerable<T> QueryGenerated<T>(
        this IDbConnection connection,
        string sql,
        object? param = null,
        IDbTransaction? transaction = null,
        int? commandTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return connection.Query<T>(sql, param, transaction, true, commandTimeout);
    }

    /// <summary>
    /// Executes a generated query asynchronously and returns rows.
    /// </summary>
    /// <typeparam name="T">Row type.</typeparam>
    /// <param name="connection">Database connection.</param>
    /// <param name="sql">SQL query.</param>
    /// <param name="param">SQL parameters.</param>
    /// <param name="transaction">Optional transaction.</param>
    /// <param name="commandTimeout">Optional timeout.</param>
    /// <param name="cancellationToken">Unused cancellation token reserved for API symmetry.</param>
    /// <returns>Result rows.</returns>
    public static Task<IEnumerable<T>> QueryGeneratedAsync<T>(
        this IDbConnection connection,
        string sql,
        object? param = null,
        IDbTransaction? transaction = null,
        int? commandTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _ = cancellationToken;

        return connection.QueryAsync<T>(sql, param, transaction, commandTimeout);
    }

    /// <summary>
    /// Executes a generated command.
    /// </summary>
    /// <param name="connection">Database connection.</param>
    /// <param name="sql">SQL command.</param>
    /// <param name="param">Parameters.</param>
    /// <param name="transaction">Optional transaction.</param>
    /// <param name="commandTimeout">Optional timeout.</param>
    /// <returns>Affected row count.</returns>
    public static int ExecuteGenerated(
        this IDbConnection connection,
        string sql,
        object? param = null,
        IDbTransaction? transaction = null,
        int? commandTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return connection.Execute(sql, param, transaction, commandTimeout);
    }

    /// <summary>
    /// Executes a generated command asynchronously.
    /// </summary>
    /// <param name="connection">Database connection.</param>
    /// <param name="sql">SQL command.</param>
    /// <param name="param">Parameters.</param>
    /// <param name="transaction">Optional transaction.</param>
    /// <param name="commandTimeout">Optional timeout.</param>
    /// <param name="cancellationToken">Unused cancellation token reserved for API symmetry.</param>
    /// <returns>Affected row count.</returns>
    public static Task<int> ExecuteGeneratedAsync(
        this IDbConnection connection,
        string sql,
        object? param = null,
        IDbTransaction? transaction = null,
        int? commandTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _ = cancellationToken;

        return connection.ExecuteAsync(sql, param, transaction, commandTimeout);
    }

    /// <summary>
    /// Executes a stored procedure and captures output parameters.
    /// </summary>
    /// <typeparam name="T">Result row type.</typeparam>
    /// <param name="connection">Database connection.</param>
    /// <param name="procedureName">Stored procedure name.</param>
    /// <param name="parameters">Dapper dynamic parameters.</param>
    /// <param name="outputParameterNames">Output parameter names.</param>
    /// <param name="transaction">Optional transaction.</param>
    /// <param name="commandTimeout">Optional timeout.</param>
    /// <returns>Procedure rows and output values.</returns>
    public static GeneratedProcedureResult<T> QueryStoredProcedureGenerated<T>(
        this IDbConnection connection,
        string procedureName,
        DynamicParameters parameters,
        IEnumerable<string> outputParameterNames,
        IDbTransaction? transaction = null,
        int? commandTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        ArgumentNullException.ThrowIfNull(parameters);

        var rows = connection.Query<T>(
                procedureName,
                parameters,
                transaction,
                commandType: CommandType.StoredProcedure,
                commandTimeout: commandTimeout)
            .ToList();

        var outputs = outputParameterNames.ToDictionary<string, string, object?>(
            static n => n,
            n => parameters.Get<dynamic>(n) as object);
        return new GeneratedProcedureResult<T>(rows, outputs);
    }

    /// <summary>
    /// Executes a stored procedure asynchronously and captures output parameters.
    /// </summary>
    /// <typeparam name="T">Result row type.</typeparam>
    /// <param name="connection">Database connection.</param>
    /// <param name="procedureName">Stored procedure name.</param>
    /// <param name="parameters">Dapper dynamic parameters.</param>
    /// <param name="outputParameterNames">Output parameter names.</param>
    /// <param name="transaction">Optional transaction.</param>
    /// <param name="commandTimeout">Optional timeout.</param>
    /// <param name="cancellationToken">Command cancellation token.</param>
    /// <returns>Procedure rows and output values.</returns>
    public static async Task<GeneratedProcedureResult<T>> QueryStoredProcedureGeneratedAsync<T>(
        this IDbConnection connection,
        string procedureName,
        DynamicParameters parameters,
        IEnumerable<string> outputParameterNames,
        IDbTransaction? transaction = null,
        int? commandTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        ArgumentNullException.ThrowIfNull(parameters);

        var command = new CommandDefinition(
            procedureName,
            parameters,
            transaction,
            commandTimeout,
            CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var rows = (await connection.QueryAsync<T>(command).ConfigureAwait(false)).ToList();

        var outputs = outputParameterNames.ToDictionary<string, string, object?>(
            static n => n,
            n => parameters.Get<dynamic>(n) as object);
        return new GeneratedProcedureResult<T>(rows, outputs);
    }
}
