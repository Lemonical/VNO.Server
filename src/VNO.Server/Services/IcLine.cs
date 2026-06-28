namespace VNO.Server.Services;

/// <summary>
/// One in character line the server relayed, for the admin monitor
/// </summary>
/// <remarks>
/// Carries the server stamped speaker identity next to the joined player name so
/// the console shows who really spoke, the same identity clients render
/// </remarks>
public sealed record IcLine(string Character, string Player, string Text);
