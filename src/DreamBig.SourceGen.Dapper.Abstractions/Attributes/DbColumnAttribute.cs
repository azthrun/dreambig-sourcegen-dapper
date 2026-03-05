namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Maps an entity property to a SQL column.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DbColumnAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DbColumnAttribute"/> class.
    /// </summary>
    /// <param name="columnName">SQL column name.</param>
    public DbColumnAttribute(string columnName)
    {
        ColumnName = string.IsNullOrWhiteSpace(columnName)
            ? throw new ArgumentException("Column name cannot be null or whitespace.", nameof(columnName))
            : columnName;
    }

    /// <summary>
    /// Gets the SQL column name.
    /// </summary>
    public string ColumnName { get; }
}
