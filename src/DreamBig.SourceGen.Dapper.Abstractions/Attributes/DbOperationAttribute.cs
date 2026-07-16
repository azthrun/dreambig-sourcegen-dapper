namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Declares the generated operation for a repository method explicitly, overriding name conventions.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class DbOperationAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DbOperationAttribute"/> class.
    /// </summary>
    /// <param name="operation">Operation kind to generate.</param>
    public DbOperationAttribute(DbOperationKind operation)
    {
        Operation = operation;
    }

    /// <summary>
    /// Gets the operation kind to generate.
    /// </summary>
    public DbOperationKind Operation { get; }

    /// <summary>
    /// Gets or sets an explicit entity type, overriding entity resolution from the method signature or name.
    /// </summary>
    public Type? Entity { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an insert should return the database-generated key
    /// instead of the affected row count.
    /// </summary>
    public bool ReturnIdentity { get; set; }
}
