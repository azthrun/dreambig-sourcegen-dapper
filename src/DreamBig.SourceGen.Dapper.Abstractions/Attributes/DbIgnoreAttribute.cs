namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Excludes an entity property from generated SQL projections and writes.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DbIgnoreAttribute : Attribute
{
}
