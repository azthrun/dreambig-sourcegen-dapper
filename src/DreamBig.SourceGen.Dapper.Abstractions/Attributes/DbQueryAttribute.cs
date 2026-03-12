namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Declares generated query composition options.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class DbQueryAttribute : Attribute
{
    /// <summary>
    /// Gets or sets an optional explicit FROM expression.
    /// </summary>
    public string? From { get; set; }

    /// <summary>
    /// Gets or sets optional WHERE expression.
    /// </summary>
    public string? Where { get; set; }

    /// <summary>
    /// Gets or sets optional ORDER BY expression.
    /// </summary>
    public string? OrderBy { get; set; }

    /// <summary>
    /// Gets or sets ORDER BY direction.
    /// </summary>
    public OrderByDirection OrderByDirection { get; set; } = OrderByDirection.Asc;

    /// <summary>
    /// Gets or sets optional JOIN expression override.
    /// </summary>
    public string? Join { get; set; }
}
