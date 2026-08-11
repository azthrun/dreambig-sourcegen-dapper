using System.ComponentModel.DataAnnotations;

namespace DreamBig.SourceGen.Dapper.MySql;

/// <summary>
/// Options for configuring MySQL/MariaDB connections.
/// </summary>
public sealed class DreamBigDapperMySqlOptions
{
    /// <summary>
    /// Gets the configuration section name used for binding.
    /// </summary>
    public const string SectionName = "DreamBig:Dapper:MySql";

    /// <summary>
    /// Gets or sets the MySQL/MariaDB connection string.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}
