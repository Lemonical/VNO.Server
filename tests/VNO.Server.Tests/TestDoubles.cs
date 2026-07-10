using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VNO.Core.Models;
using VNO.Core.Protocol;
using VNO.Server.Services;

namespace VNO.Server.Tests;

/// <summary>
/// A hand rolled game host whose events the tests raise directly
/// </summary>
internal sealed class FakeGameHost : IGameHost
{
    public ServerStatus Status { get; set; } = ServerStatus.Offline;

    public int PlayerCount { get; set; }

    public List<(int UserId, NetworkMessage Message)> Sent { get; } = new();

    public List<string> Notices { get; } = new();

    public event EventHandler<ServerStatus>? StatusChanged;
    public event EventHandler? UsersChanged;
    public event EventHandler<string>? LogEntry;
    public event EventHandler<OocLine>? OocReceived;
    public event EventHandler<IcLine>? IcReceived;

    public void RaiseUsersChanged() => UsersChanged?.Invoke(this, EventArgs.Empty);

    public void RaiseLog(string entry) => LogEntry?.Invoke(this, entry);

    public void RaiseOoc(OocLine line) => OocReceived?.Invoke(this, line);

    public void RaiseIc(IcLine line) => IcReceived?.Invoke(this, line);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Status = ServerStatus.Online;
        StatusChanged?.Invoke(this, Status);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        Status = ServerStatus.Offline;
        StatusChanged?.Invoke(this, Status);
        return Task.CompletedTask;
    }

    public Task SendToUserAsync(
        int userId, NetworkMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Add((userId, message));
        return Task.CompletedTask;
    }

    public Task DisconnectUserAsync(int userId) => Task.CompletedTask;

    public Task BroadcastNoticeAsync(string text, CancellationToken cancellationToken = default)
    {
        Notices.Add(text);
        return Task.CompletedTask;
    }

    public Task SendOocAsync(string text, CancellationToken cancellationToken = default)
    {
        OocReceived?.Invoke(this, new OocLine("Server", text));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Records moderation calls instead of acting on a host
/// </summary>
internal sealed class FakeModeration : IModerationService
{
    public (string Reason, string PlacedBy, TimeSpan? Duration)? LastAccountBan { get; private set; }

    public (string Reason, string PlacedBy, TimeSpan? Duration)? LastAddressBan { get; private set; }

    public Task KickAsync(int userId, string reason) => Task.CompletedTask;

    public Task MuteAsync(int userId) => Task.CompletedTask;

    public Task UnmuteAsync(int userId) => Task.CompletedTask;

    public Task BanAccountAsync(int userId, string reason, string placedBy, TimeSpan? duration = null)
    {
        LastAccountBan = (reason, placedBy, duration);
        return Task.CompletedTask;
    }

    public void UnbanAccount(string userName)
    {
    }

    public Task BanAddressAsync(string ipAddress, string reason, string placedBy, TimeSpan? duration = null)
    {
        LastAddressBan = (reason, placedBy, duration);
        return Task.CompletedTask;
    }

    public void UnbanAddress(string ipAddress)
    {
    }
}

/// <summary>
/// An auth link that never dials anything and grants every sign in
/// </summary>
internal sealed class FakeAuthLink : IAuthServerLink
{
    public List<(int OnlinePlayers, int PlayerCapacity)> PublishedMetrics { get; } = new();

    public ConnectionState State { get; set; } = ConnectionState.Disconnected;

    public string? Username { get; set; }

#pragma warning disable CS0067 // the controller subscribes, the tests never raise it
    public event EventHandler<ConnectionState>? StateChanged;
#pragma warning restore CS0067

    public Task<AuthConnectResult> ConnectAsync(
        string username, string password, CancellationToken cancellationToken = default)
    {
        State = ConnectionState.Connected;
        Username = username;
        return Task.FromResult(AuthConnectResult.Granted);
    }

    public Task<GameTokenValidationResult> ValidateGameTokenAsync(
        string token,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            string.IsNullOrWhiteSpace(token)
                ? GameTokenValidationResult.Invalid
                : new GameTokenValidationResult(true, token));

    public Task PublishPlayerMetricsAsync(
        int onlinePlayers,
        int playerCapacity,
        CancellationToken cancellationToken = default)
    {
        PublishedMetrics.Add((onlinePlayers, playerCapacity));
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        State = ConnectionState.Disconnected;
        Username = null;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Counts saves without touching the disk
/// </summary>
internal sealed class FakeSettingsStore : IServerSettingsStore
{
    public int SaveCount { get; private set; }

    public Task SaveAsync(ServerSettings settings, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
