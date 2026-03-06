using System.Data;

namespace DreamBig.SourceGen.Dapper.Internal;

/// <summary>
/// Exposes the current transaction for generated repository implementations.
/// </summary>
public interface IGeneratedTransactionContext
{
    /// <summary>
    /// Gets the current active database transaction.
    /// </summary>
    IDbTransaction? CurrentTransaction { get; }
}
