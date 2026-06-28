using System;
using System.Collections.Generic;
using VNO.Core.Networking;

namespace VNO.Server.Services;

/// <summary>
/// Settings that control how the game host runs
/// </summary>
/// <remarks>
/// Loaded from the legacy server data files by <see cref="ServerSettingsLoader"/>,
/// data\init.ini for the host identity and auth link, data\areas.ini for the room
/// list, and data\musiclist.txt for the tracks. These replace the hard coded ports
/// and addresses found in the legacy Form3
/// </remarks>
public sealed class ServerSettings
{
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
    /// Which transport the server reaches the auth server over
    /// </summary>
    public Transport AuthTransport { get; set; } = Transport.Tcp;

    /// <summary>
    /// Dial the auth server over TLS, wss instead of ws, used behind managed ingress
    /// </summary>
    public bool AuthUseTls { get; set; }

    /// <summary>
    /// Whether the server asks the auth server to list it publicly
    /// </summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// Host name or address of the auth server
    /// </summary>
    public string AuthServerHost { get; set; } = "127.0.0.1";

    /// <summary>
    /// TCP port of the auth server
    /// </summary>
    public int AuthServerPort { get; set; } = 6543;

    /// <summary>
    /// How often to send a heartbeat to the auth server, in seconds
    /// </summary>
    public int HeartbeatSeconds { get; set; } = 10;

    /// <summary>
    /// Areas the server hosts, sent to each player on join
    /// </summary>
    public List<string> Areas { get; set; } = new() { "Courtroom" };

    /// <summary>
    /// Music tracks the server offers, sent to each player on join
    /// </summary>
    public List<string> Music { get; set; } = new();

    /// <summary>
    /// Character roster the server offers, sent to each player on join. Empty
    /// means the client falls back to its local roster
    /// </summary>
    public List<string> Characters { get; set; } = new();

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
