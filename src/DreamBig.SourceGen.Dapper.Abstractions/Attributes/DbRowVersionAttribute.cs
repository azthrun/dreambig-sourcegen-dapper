namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Marks a database-generated concurrency token property (for example SQL Server <c>rowversion</c>).
/// The column is excluded from generated INSERT and UPDATE SET clauses and is appended to the WHERE
/// clause of updates and entity-based deletes so stale writes affect zero rows.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DbRowVersionAttribute : Attribute
{
}
