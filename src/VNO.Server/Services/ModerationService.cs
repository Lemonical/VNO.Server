using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VNO.Core.Models;
using VNO.Core.Protocol;

namespace VNO.Server.Services;

/// <summary>
/// Default moderation service that drives the host and the ban registry
/// </summary>
public sealed class ModerationService : IModerationService
{
    private readonly IGameHost _host;
    private readonly IUserRegistry _users;
    private readonly IBanRegistry _bans;
    private readonly ILogger<ModerationService> _logger;

    /// <summary>
    /// Creates the service with its dependencies
    /// </summary>
    public ModerationService(
        IGameHost host,
        IUserRegistry users,
        IBanRegistry bans,
        ILogger<ModerationService> logger)
    {
        _host = host;
        _users = users;
        _bans = bans;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task KickAsync(int userId, string reason)
    {
        await _host.SendToUserAsync(userId, new NetworkMessage(MessageType.Kick, reason)).ConfigureAwait(false);
        await _host.DisconnectUserAsync(userId).ConfigureAwait(false);
        _logger.LogInformation("Kicked player {Id}", userId);
    }

    /// <inheritdoc />
    public async Task MuteAsync(int userId)
    {
        SetMuted(userId, true);
        await _host.SendToUserAsync(userId, NetworkMessage.Create(MessageType.Mute)).ConfigureAwait(false);
        _logger.LogInformation("Muted player {Id}", userId);
    }

    /// <inheritdoc />
    public async Task UnmuteAsync(int userId)
    {
        SetMuted(userId, false);
        await _host.SendToUserAsync(userId, NetworkMessage.Create(MessageType.Unmute)).ConfigureAwait(false);
        _logger.LogInformation("Unmuted player {Id}", userId);
    }

    /// <inheritdoc />
    public async Task BanAccountAsync(int userId, string reason, string placedBy, TimeSpan? duration = null)
    {
        var sessionId = _users.FindSessionByUserId(userId);
        var user = sessionId is null ? null : _users.FindBySession(sessionId);
        if (user is null)
        {
            return;
        }

        _bans.Add(new BanEntry
        {
            Kind = BanKind.Account,
            Target = user.Name,
            Reason = reason,
            PlacedBy = placedBy,
            ExpiresAt = duration is null ? null : DateTimeOffset.UtcNow + duration,
        });

        await _host.SendToUserAsync(userId, new NetworkMessage(MessageType.Ban, reason)).ConfigureAwait(false);
        await _host.DisconnectUserAsync(userId).ConfigureAwait(false);
        _logger.LogInformation("Banned account {Name}", user.Name);
    }

    /// <inheritdoc />
    public void UnbanAccount(string userName)
    {
        _bans.Remove(BanKind.Account, userName);
        _logger.LogInformation("Unbanned account {Name}", userName);
    }

    /// <inheritdoc />
    public async Task BanAddressAsync(string ipAddress, string reason, string placedBy, TimeSpan? duration = null)
    {
        _bans.Add(new BanEntry
        {
            Kind = BanKind.IpAddress,
            Target = ipAddress,
            Reason = reason,
            PlacedBy = placedBy,
            ExpiresAt = duration is null ? null : DateTimeOffset.UtcNow + duration,
        });

        // drop everyone currently on that address
        foreach (var user in _users.Users)
        {
            if (string.Equals(user.IpAddress, ipAddress, System.StringComparison.Ordinal))
            {
                await _host.DisconnectUserAsync(user.Id).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("Banned address {Address}", ipAddress);
    }

    /// <inheritdoc />
    public void UnbanAddress(string ipAddress)
    {
        _bans.Remove(BanKind.IpAddress, ipAddress);
        _logger.LogInformation("Unbanned address {Address}", ipAddress);
    }

    private void SetMuted(int userId, bool muted)
    {
        var sessionId = _users.FindSessionByUserId(userId);
        var user = sessionId is null ? null : _users.FindBySession(sessionId);
        if (user is not null)
        {
            user.IsMuted = muted;
        }
    }
}
