namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Marks a repository method as a stored procedure execution.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="DbStoredProcedureAttribute"/> class.
/// </remarks>
/// <param name="name">Procedure name.</param>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class DbStoredProcedureAttribute(string name) : Attribute
{

    /// <summary>
    /// Gets the procedure name.
    /// </summary>
    public string Name { get; } = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Stored procedure name cannot be null or whitespace.", nameof(name))
            : name;

    /// <summary>
    /// Gets or sets procedure schema.
    /// </summary>
    public string? Schema { get; set; }
}
