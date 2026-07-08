namespace VNO.Server.ViewModels;

/// <summary>
/// The sections the console sidebar navigates between
/// </summary>
public enum ConsoleSection
{
    /// <summary>
    /// Server overview, stats, issues, and recent activity
    /// </summary>
    Dashboard,

    /// <summary>
    /// Connected players and moderation
    /// </summary>
    Players,

    /// <summary>
    /// OOC, in character, and event log monitors
    /// </summary>
    Chat,

    /// <summary>
    /// Server settings and content lists
    /// </summary>
    Configuration,

    /// <summary>
    /// Active bans
    /// </summary>
    Bans,

    /// <summary>
    /// Theme, accent, and density
    /// </summary>
    Appearance,
}
