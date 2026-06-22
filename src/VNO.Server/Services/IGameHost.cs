using System;
using System.Threading;
using System.Threading.Tasks;
using VNO.Core.Models;
using VNO.Core.Protocol;

namespace VNO.Server.Services;

/// <summary>
/// The game host, it listens for players and relays messages between them
/// </summary>
/// <remarks>
/// This is the heart of the legacy server. It owns the listening socket through
/// the core message server and turns raw sessions into tracked users
/// </remarks>
public interface IGameHost
{
    /// <summary>
    /// Whether the host is online and accepting players
    /// </summary>
    ServerStatus Status { get; }

    /// <summary>
    /// Number of connected players
    /// </summary>
    int PlayerCount { get; }

    /// <summary>
    /// Raised when the online status changes
    /// </summary>
    event EventHandler<ServerStatus>? StatusChanged;

    /// <summary>
    /// Raised when the user list changes so the UI can refresh
    /// </summary>
    event EventHandler? UsersChanged;

    /// <summary>
    /// Raised with a human readable line for the event log
    /// </summary>
    event EventHandler<string>? LogEntry;

    /// <summary>
    /// Raised for every out of character line, so the admin window can monitor chat
    /// </summary>
    event EventHandler<OocLine>? OocReceived;

    /// <summary>
    /// Starts listening
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops listening and drops all players
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Sends a message to one player by user id
    /// </summary>
    Task SendToUserAsync(int userId, NetworkMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects one player by user id
    /// </summary>
    Task DisconnectUserAsync(int userId);

    /// <summary>
    /// Sends a notice to every player
    /// </summary>
    Task BroadcastNoticeAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an out of character line to every player as the server
    /// </summary>
    Task SendOocAsync(string text, CancellationToken cancellationToken = default);
}
