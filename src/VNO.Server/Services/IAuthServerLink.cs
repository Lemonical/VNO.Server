using System;
using System.Threading;
using System.Threading.Tasks;
using VNO.Core.Models;

namespace VNO.Server.Services;

/// <summary>
/// Keeps a link to the central auth server, the AS
/// </summary>
/// <remarks>
/// The legacy server used a client socket to register with the AS and send a
/// timed heartbeat. Hosting now requires an account, so the link performs the
/// full handshake, version check, account login, then the public listing, and
/// reports the exact outcome so no frontend has to guess
/// </remarks>
public interface IAuthServerLink
{
    /// <summary>
    /// Current state of the link to the auth server
    /// </summary>
    ConnectionState State { get; }

    /// <summary>
    /// Account name the link is signed in under, null while signed out
    /// </summary>
    string? Username { get; }

    /// <summary>
    /// Raised when the link state changes
    /// </summary>
    event EventHandler<ConnectionState>? StateChanged;

    /// <summary>
    /// Connects, signs in with the account, registers the listing when public,
    /// and begins the heartbeat. Returns what actually happened
    /// </summary>
    Task<AuthConnectResult> ConnectAsync(
        string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically asks Master to validate and consume a client's handoff credential
    /// </summary>
    Task<GameTokenValidationResult> ValidateGameTokenAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the current public-directory player metrics when this server is listed
    /// </summary>
    Task PublishPlayerMetricsAsync(
        int onlinePlayers,
        int playerCapacity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the link to the auth server and stops reconnecting
    /// </summary>
    Task DisconnectAsync();
}
