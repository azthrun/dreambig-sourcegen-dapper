namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Maps an entity type to a SQL table.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="DbTableAttribute"/> class.
/// </remarks>
/// <param name="tableName">SQL table name.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DbTableAttribute(string tableName) : Attribute
{

    /// <summary>
    /// Gets the SQL table name.
    /// </summary>
    public string TableName { get; } = string.IsNullOrWhiteSpace(tableName)
            ? throw new ArgumentException("Table name cannot be null or whitespace.", nameof(tableName))
            : tableName;

    /// <summary>
    /// Gets or sets the schema name.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>
    /// Gets or sets the primary key property name or mapped column name.
    /// This can be used instead of decorating an entity property with <c>[DbKey]</c>.
    /// </summary>
    public string? PrimaryKey { get; set; }
}
