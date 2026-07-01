namespace VNO.Server.Admin;

/// <summary>
/// Severity of a captured issue
/// </summary>
public enum IssueLevel
{
    /// <summary>
    /// Something looked off but the server kept going
    /// </summary>
    Warning,

    /// <summary>
    /// An operation failed
    /// </summary>
    Error,
}
