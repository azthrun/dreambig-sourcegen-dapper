using System.ComponentModel.DataAnnotations;

namespace DreamBig.SourceGen.Dapper.PostgreSql;

/// <summary>
/// Options for configuring PostgreSQL connections.
/// </summary>
public sealed class DreamBigDapperPostgreSqlOptions
{
    /// <summary>
    /// Gets the configuration section name used for binding.
    /// </summary>
    public const string SectionName = "DreamBig:Dapper:PostgreSql";

    /// <summary>
    /// Gets or sets the PostgreSQL connection string.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}
