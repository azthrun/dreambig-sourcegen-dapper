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
    /// Gets or sets the joined table entity type.
    /// </summary>
    public Type? JoinTable { get; set; }

    /// <summary>
    /// Gets or sets the default schema for the joined table when not inferred from the entity.
    /// </summary>
    public string Schema { get; set; } = "dbo";

    /// <summary>
    /// Gets or sets the left-side join column name (CLR property name).
    /// </summary>
    public string? JoinColumnA { get; set; }

    /// <summary>
    /// Gets or sets the right-side join column name (CLR property name).
    /// </summary>
    public string? JoinColumnB { get; set; }

    /// <summary>
    /// Gets or sets an optional join filter appended to the ON clause.
    /// </summary>
    public string? Where { get; set; }
}
