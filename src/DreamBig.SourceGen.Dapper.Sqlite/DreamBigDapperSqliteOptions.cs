using System.ComponentModel.DataAnnotations;

namespace DreamBig.SourceGen.Dapper.Sqlite;

/// <summary>
/// Options for configuring SQLite connections.
/// </summary>
public sealed class DreamBigDapperSqliteOptions
{
    /// <summary>
    /// Gets the configuration section name used for binding.
    /// </summary>
    public const string SectionName = "DreamBig:Dapper:Sqlite";

    /// <summary>
    /// Gets or sets the SQLite connection string.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}
