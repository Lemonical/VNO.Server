using System;
using VNO.Core.Models;

namespace VNO.Server.Admin;

/// <summary>
/// A point in time summary of the running server for the console dashboard
/// </summary>
public sealed record ServerOverview(
    string Name,
    int ListenPort,
    string TransportLabel,
    ServerStatus Status,
    DateTimeOffset? StartedAt,
    bool IsPublic,
    ConnectionState AuthState,
    int PlayerCount,
    int PeakPlayers,
    int OocMessageCount,
    int IcMessageCount,
    string? AuthUsername = null)
{
    /// <summary>
    /// Time online since the last start, zero when offline
    /// </summary>
    public TimeSpan Uptime =>
        Status == ServerStatus.Online && StartedAt is not null
            ? DateTimeOffset.UtcNow - StartedAt.Value
            : TimeSpan.Zero;
}
