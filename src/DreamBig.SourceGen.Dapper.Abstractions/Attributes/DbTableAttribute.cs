namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Maps an entity type to a SQL table.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DbTableAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DbTableAttribute"/> class.
    /// </summary>
    /// <param name="tableName">SQL table name.</param>
    public DbTableAttribute(string tableName)
    {
        TableName = string.IsNullOrWhiteSpace(tableName)
            ? throw new ArgumentException("Table name cannot be null or whitespace.", nameof(tableName))
            : tableName;
    }

    /// <summary>
    /// Gets the SQL table name.
    /// </summary>
    public string TableName { get; }

    /// <summary>
    /// Gets or sets the schema name. Defaults to <c>dbo</c>.
    /// </summary>
    public string Schema { get; set; } = "dbo";
}
