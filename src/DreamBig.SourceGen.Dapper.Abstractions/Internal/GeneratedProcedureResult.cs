namespace DreamBig.SourceGen.Dapper.Internal;

/// <summary>
/// Wraps stored procedure result and output parameter values.
/// </summary>
/// <typeparam name="T">Result row type.</typeparam>
/// <remarks>
/// Initializes a new instance of the <see cref="GeneratedProcedureResult{T}"/> class.
/// </remarks>
/// <param name="rows">Rows returned from the stored procedure.</param>
/// <param name="outputValues">Output parameter values.</param>
public sealed class GeneratedProcedureResult<T>(IReadOnlyList<T> rows, IReadOnlyDictionary<string, object?> outputValues)
{

    /// <summary>
    /// Gets result rows.
    /// </summary>
    public IReadOnlyList<T> Rows { get; } = rows;

    /// <summary>
    /// Gets output parameter values by parameter name.
    /// </summary>
    public IReadOnlyDictionary<string, object?> OutputValues { get; } = outputValues;
}
