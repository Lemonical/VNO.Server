namespace VNO.Server.Admin;

/// <summary>
/// The editable server settings as one value the console reads and writes back
/// </summary>
/// <remarks>
/// A frontend edits a copy and applies the whole thing, so a partially typed form
/// never leaks into the live <see cref="VNO.Server.Services.ServerSettings"/>
/// </remarks>
public sealed record ServerConfig(
    string Name,
    int ListenPort,
    bool IsPublic,
    int HeartbeatSeconds,
    string ModeratorPassword,
    string AuthServerHost,
    int AuthServerPort,
    bool AuthUseTls);
