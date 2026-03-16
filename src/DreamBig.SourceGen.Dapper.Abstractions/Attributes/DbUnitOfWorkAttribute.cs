namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Marks an interface for source-generated Unit of Work implementation.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class DbUnitOfWorkAttribute : Attribute;
