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
/// timed heartbeat. This service owns that outbound link and reports its state
/// for the admin status bar
/// </remarks>
public interface IAuthServerLink
{
    /// <summary>
    /// Current state of the link to the auth server
    /// </summary>
    ConnectionState State { get; }

    /// <summary>
    /// Raised when the link state changes
    /// </summary>
    event EventHandler<ConnectionState>? StateChanged;

    /// <summary>
    /// Connects to the auth server and begins the heartbeat
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the link to the auth server
    /// </summary>
    Task DisconnectAsync();
}
