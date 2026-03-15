namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Marks an interface for source-generated Dapper repository implementation.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class DbRepositoryAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a value indicating whether identifiers should preserve case sensitivity.
    /// </summary>
    public bool CaseSensitive { get; set; } = true;
}
