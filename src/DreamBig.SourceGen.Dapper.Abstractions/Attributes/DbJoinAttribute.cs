namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Declares a join used by a generated query method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class DbJoinAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the join type.
    /// </summary>
    public JoinType JoinType { get; set; }

    /// <summary>
    /// Gets or sets the left-side table entity type.
    /// </summary>
    public Type? JoinTableA { get; set; }

    /// <summary>
    /// Gets or sets the right-side table entity type.
    /// </summary>
    public Type? JoinTableB { get; set; }

    /// <summary>
    /// Gets or sets the default schema for the left-side table when not inferred from the entity.
    /// </summary>
    public string? SchemaA { get; set; }

    /// <summary>
    /// Gets or sets the default schema for the right-side table when not inferred from the entity.
    /// </summary>
    public string? SchemaB { get; set; }

    /// <summary>
    /// Gets or sets the left-side join column name (CLR property name).
    /// </summary>
    public string? JoinColumnA { get; set; }

    /// <summary>
    /// Gets or sets the right-side join column name (CLR property name).
    /// </summary>
    public string? JoinColumnB { get; set; }

    /// <summary>
    /// Gets or sets the logical alias for the left-side table.
    /// </summary>
    public string? AliasA { get; set; }

    /// <summary>
    /// Gets or sets the logical alias for the right-side table.
    /// </summary>
    public string? AliasB { get; set; }

    /// <summary>
    /// Gets or sets an optional query-level filter appended to the WHERE clause.
    /// </summary>
    public string? Where { get; set; }

    /// <summary>
    /// Gets or sets an optional join filter appended to the ON clause.
    /// </summary>
    public string? On { get; set; }

    /// <summary>
    /// Gets or sets an optional ORDER BY expression.
    /// </summary>
    public string? OrderBy { get; set; }

    /// <summary>
    /// Gets or sets ORDER BY direction.
    /// </summary>
    public OrderByDirection OrderByDirection { get; set; } = OrderByDirection.Asc;
}
