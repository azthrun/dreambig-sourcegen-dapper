using System.ComponentModel.DataAnnotations;

namespace DreamBig.SourceGen.Dapper.SqlServer;

/// <summary>
/// Options for configuring SQL Server connections.
/// </summary>
public sealed class DreamBigDapperSqlServerOptions
{
    /// <summary>
    /// Gets the configuration section name used for binding.
    /// </summary>
    public const string SectionName = "DreamBig:Dapper:SqlServer";

    /// <summary>
    /// Gets or sets the SQL Server connection string.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}
