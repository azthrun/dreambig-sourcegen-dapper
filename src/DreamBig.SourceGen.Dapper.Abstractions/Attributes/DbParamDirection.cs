namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Direction for stored procedure parameters.
/// </summary>
public enum DbParamDirection
{
    /// <summary>
    /// Input only.
    /// </summary>
    Input,

    /// <summary>
    /// Output only.
    /// </summary>
    Output,

    /// <summary>
    /// Input and output.
    /// </summary>
    InputOutput,
}
