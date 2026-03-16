namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Maps an entity property to a SQL column.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="DbColumnAttribute"/> class.
/// </remarks>
/// <param name="columnName">SQL column name.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DbColumnAttribute(string columnName) : Attribute
{

    /// <summary>
    /// Gets the SQL column name.
    /// </summary>
    public string ColumnName { get; } = string.IsNullOrWhiteSpace(columnName)
            ? throw new ArgumentException("Column name cannot be null or whitespace.", nameof(columnName))
            : columnName;
}
