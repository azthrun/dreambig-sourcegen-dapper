namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Marks an interface for source-generated Dapper repository implementation.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class DbRepositoryAttribute : Attribute
{
}
