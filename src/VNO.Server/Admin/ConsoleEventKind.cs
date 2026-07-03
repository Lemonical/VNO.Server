namespace VNO.Server.Admin;

/// <summary>
/// Coarse category of a console event, drives the event log styling
/// </summary>
public enum ConsoleEventKind
{
    /// <summary>
    /// Routine server activity
    /// </summary>
    Info,

    /// <summary>
    /// A player connected or disconnected
    /// </summary>
    Join,

    /// <summary>
    /// A staff or moderation action
    /// </summary>
    Moderation,

    /// <summary>
    /// Something went wrong
    /// </summary>
    Error,
}
