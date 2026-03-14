namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Marks a repository method as a stored procedure execution.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class DbStoredProcedureAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DbStoredProcedureAttribute"/> class.
    /// </summary>
    /// <param name="name">Procedure name.</param>
    public DbStoredProcedureAttribute(string name)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Stored procedure name cannot be null or whitespace.", nameof(name))
            : name;
    }

    /// <summary>
    /// Gets the procedure name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets procedure schema.
    /// </summary>
    public string? Schema { get; set; }
}
