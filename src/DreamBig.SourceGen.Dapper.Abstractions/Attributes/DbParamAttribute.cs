using System.Data;

namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Declares stored procedure parameter metadata.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="DbParamAttribute"/> class.
/// </remarks>
/// <param name="name">Parameter name, including <c>@</c> prefix.</param>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class DbParamAttribute(string name) : Attribute
{

    /// <summary>
    /// Gets parameter name.
    /// </summary>
    public string Name { get; } = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Parameter name cannot be null or whitespace.", nameof(name))
            : name;

    /// <summary>
    /// Gets or sets parameter direction.
    /// </summary>
    public DbParamDirection Direction { get; set; } = DbParamDirection.Input;

    /// <summary>
    /// Gets or sets database type.
    /// </summary>
    public DbType? DbType { get; set; }

    /// <summary>
    /// Gets or sets optional parameter size.
    /// </summary>
    public int? Size { get; set; }
}
