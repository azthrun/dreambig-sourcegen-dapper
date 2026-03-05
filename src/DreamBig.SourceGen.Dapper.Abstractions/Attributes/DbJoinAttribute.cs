namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Declares a join used by a generated query method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class DbJoinAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DbJoinAttribute"/> class.
    /// </summary>
    /// <param name="joinType">Join type.</param>
    /// <param name="table">Joined table expression (table name or table + alias).</param>
    /// <param name="left">Left expression of join condition.</param>
    /// <param name="right">Right expression of join condition.</param>
    public DbJoinAttribute(JoinType joinType, string table, string left, string right)
    {
        JoinType = joinType;
        Table = string.IsNullOrWhiteSpace(table)
            ? throw new ArgumentException("Join table cannot be null or whitespace.", nameof(table))
            : table;
        Left = string.IsNullOrWhiteSpace(left)
            ? throw new ArgumentException("Join left condition cannot be null or whitespace.", nameof(left))
            : left;
        Right = string.IsNullOrWhiteSpace(right)
            ? throw new ArgumentException("Join right condition cannot be null or whitespace.", nameof(right))
            : right;
    }

    /// <summary>
    /// Gets the join type.
    /// </summary>
    public JoinType JoinType { get; }

    /// <summary>
    /// Gets the join table expression.
    /// </summary>
    public string Table { get; }

    /// <summary>
    /// Gets the left side of the join condition.
    /// </summary>
    public string Left { get; }

    /// <summary>
    /// Gets the right side of the join condition.
    /// </summary>
    public string Right { get; }

    /// <summary>
    /// Gets or sets optional alias metadata for readability and diagnostics.
    /// </summary>
    public string? Alias { get; set; }
}
