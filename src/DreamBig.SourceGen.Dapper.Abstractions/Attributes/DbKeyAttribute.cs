namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Marks an entity property as the primary key.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DbKeyAttribute : Attribute
{
}
