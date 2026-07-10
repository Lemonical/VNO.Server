using System;
using System.Collections.Generic;
using VNO.Core.Networking;

namespace VNO.Server.Services;

/// <summary>
/// Settings that control how the game host runs
/// </summary>
/// <remarks>
/// Loaded from the legacy server data files by <see cref="ServerSettingsLoader"/>,
/// data\init.ini for the host identity and Master-account credentials, data\areas.ini
/// for the room list, and data\musiclist.txt for the tracks. The public Master endpoint
/// itself is owned by VNO.Core.
/// </remarks>
public sealed class ServerSettings
{
    /// <summary>
    /// Resolved data directory used for persistent server-owned state
    /// </summary>
    public string DataDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Name shown to the auth server and in listings
    /// </summary>
    public string Name { get; set; } = "Visual Novel Online Server";

    /// <summary>
    /// Which transport the server hosts players over, TCP or WebSocket
    /// </summary>
    /// <remarks>
    /// Defaults to TCP so existing players keep connecting through the dual transport window.
    /// A self hoster flips this to WebSocket once its clients are updated
    /// </remarks>
    public Transport ListenTransport { get; set; } = Transport.Tcp;

    /// <summary>
    /// TCP port the server listens on for players, also the HTTP/WebSocket port
    /// </summary>
    public int ListenPort { get; set; } = 6541;

    /// <summary>
    /// Maximum number of authenticated players admitted by this server
    /// </summary>
    public int PlayerCapacity { get; set; } = 100;

    /// <summary>
    /// Whether the server asks the auth server to list it publicly
    /// </summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// How often to send a heartbeat to the auth server, in seconds
    /// </summary>
    public int HeartbeatSeconds { get; set; } = 10;

    /// <summary>
    /// Auth server account name saved when the operator chose remember me
    /// </summary>
    public string AuthUsername { get; set; } = string.Empty;

    /// <summary>
    /// Auth server account password saved when the operator chose remember me
    /// </summary>
    public string AuthPassword { get; set; } = string.Empty;

    /// <summary>
    /// True when the password came from a mounted secret and must never be written to data files
    /// </summary>
    public bool AuthPasswordFromExternalSecret { get; set; }

    /// <summary>
    /// Whether the saved account should be used to sign in without prompting
    /// </summary>
    public bool AuthRemember { get; set; }

    /// <summary>
    /// Areas the server hosts, sent to each player on join
    /// </summary>
    public List<string> Areas { get; set; } = new() { "Courtroom" };

    /// <summary>
    /// Music tracks the server offers, sent to each player on join
    /// </summary>
    public List<string> Music { get; set; } = new();

    /// <summary>
    /// Character roster the server offers and authoritatively enforces
    /// </summary>
    public List<string> Characters { get; set; } = new() { "Servant Archer" };

    /// <summary>
    /// Authoritative item definitions offered by this server
    /// </summary>
    public List<string> Items { get; set; } = new();

    /// <summary>
    /// Password that grants moderator powers, the legacy staff password. Empty
    /// disables in game moderator authentication
    /// </summary>
    public string ModeratorPassword { get; set; } = string.Empty;

    /// <summary>
    /// Chat lines a single player may burst before the steady rate applies
    /// </summary>
    /// <remarks>
    /// A per player token bucket caps in character, out of character, and music spam so one
    /// client cannot flood the area. Zero disables the cap. The burst is the allowance a
    /// normal back and forth needs, <see cref="ChatMessagesPerSecond"/> is the sustained rate
    /// </remarks>
    public int ChatBurst { get; set; } = 8;

    /// <summary>
    /// Sustained chat lines per second per player once the burst is spent
    /// </summary>
    public double ChatMessagesPerSecond { get; set; } = 2;
}
