namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Supported SQL join types.
/// </summary>
public enum JoinType
{
    /// <summary>
    /// INNER JOIN.
    /// </summary>
    Inner,

    /// <summary>
    /// LEFT OUTER JOIN.
    /// </summary>
    Left,

    /// <summary>
    /// RIGHT OUTER JOIN.
    /// </summary>
    Right,

    /// <summary>
    /// FULL OUTER JOIN.
    /// </summary>
    Full,
}
