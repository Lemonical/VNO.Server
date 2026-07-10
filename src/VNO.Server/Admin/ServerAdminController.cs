using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using VNO.Core.Models;
using VNO.Core.Networking;
using VNO.Core.Protocol;
using VNO.Server.Services;

namespace VNO.Server.Admin;

/// <summary>
/// Default admin controller over the game host and its services
/// </summary>
/// <remarks>
/// Owns the console facing history and counters, chat feeds, the event log, the
/// player peak, and the uptime anchor, so every frontend sees the same numbers.
/// Actions taken from the console are recorded as placed by admin
/// </remarks>
public sealed class ServerAdminController : IServerAdminController
{
    private const int MaxHistory = 500;
    private const string ConsoleActor = "admin";

    private readonly IGameHost _host;
    private readonly IModerationService _moderation;
    private readonly IAuthServerLink _authLink;
    private readonly IUserRegistry _users;
    private readonly IBanRegistry _bans;
    private readonly IIssueLog _issues;
    private readonly IServerSettingsStore _store;
    private readonly ServerSettings _settings;

    private readonly object _gate = new();
    private readonly List<ConsoleEvent> _events = new();
    private readonly List<ChatEntry> _ooc = new();
    private readonly List<IcEntry> _ic = new();

    private DateTimeOffset? _startedAt;
    private int _peakPlayers;
    private int _oocCount;
    private int _icCount;

    /// <summary>
    /// Creates the controller over the running services
    /// </summary>
    public ServerAdminController(
        IGameHost host,
        IModerationService moderation,
        IAuthServerLink authLink,
        IUserRegistry users,
        IBanRegistry bans,
        IIssueLog issues,
        IServerSettingsStore store,
        IOptions<ServerSettings> settings)
    {
        _host = host;
        _moderation = moderation;
        _authLink = authLink;
        _users = users;
        _bans = bans;
        _issues = issues;
        _store = store;
        _settings = settings.Value;

        _host.StatusChanged += OnStatusChanged;
        _host.UsersChanged += OnUsersChanged;
        _host.LogEntry += OnLogEntry;
        _host.OocReceived += OnOocReceived;
        _host.IcReceived += OnIcReceived;
        _authLink.StateChanged += (_, state) => AuthStateChanged?.Invoke(this, state);
        _bans.Changed += (_, _) => BansChanged?.Invoke(this, EventArgs.Empty);
        _issues.IssueRaised += (_, entry) => IssueRaised?.Invoke(this, entry);
    }

    /// <inheritdoc />
    public event EventHandler<ServerStatus>? StatusChanged;

    /// <inheritdoc />
    public event EventHandler<ConnectionState>? AuthStateChanged;

    /// <inheritdoc />
    public event EventHandler? PlayersChanged;

    /// <inheritdoc />
    public event EventHandler<ConsoleEvent>? EventLogged;

    /// <inheritdoc />
    public event EventHandler<ChatEntry>? OocReceived;

    /// <inheritdoc />
    public event EventHandler<IcEntry>? IcReceived;

    /// <inheritdoc />
    public event EventHandler<IssueEntry>? IssueRaised;

    /// <inheritdoc />
    public event EventHandler? BansChanged;

    /// <inheritdoc />
    public event EventHandler? ConfigChanged;

    /// <inheritdoc />
    public ServerOverview GetOverview()
    {
        lock (_gate)
        {
            return new ServerOverview(
                _settings.Name,
                _settings.ListenPort,
                _settings.ListenTransport == Transport.WebSocket ? "WebSocket" : "TCP",
                _host.Status,
                _startedAt,
                _settings.IsPublic,
                _authLink.State,
                _host.PlayerCount,
                _peakPlayers,
                _oocCount,
                _icCount,
                _authLink.Username);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<PlayerSnapshot> GetPlayers() =>
        _users.Users
            .OrderBy(u => u.Id)
            .Select(u => new PlayerSnapshot(
                u.Id,
                string.IsNullOrEmpty(u.Name) ? $"Player {u.Id}" : u.Name,
                u.Character,
                u.AreaId,
                AreaNameOf(u.AreaId),
                u.IpAddress,
                u.IsModerator,
                u.IsMuted,
                u.ConnectedAt))
            .ToList();

    /// <inheritdoc />
    public IReadOnlyList<ConsoleEvent> GetEvents()
    {
        lock (_gate)
        {
            return _events.ToArray();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ChatEntry> GetOocHistory()
    {
        lock (_gate)
        {
            return _ooc.ToArray();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IcEntry> GetIcHistory()
    {
        lock (_gate)
        {
            return _ic.ToArray();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IssueEntry> GetIssues() => _issues.Issues;

    /// <inheritdoc />
    public IReadOnlyList<BanEntry> GetBans() =>
        _bans.Entries.OrderByDescending(b => b.PlacedAt).ToList();

    /// <inheritdoc />
    public Task StartServerAsync(CancellationToken cancellationToken = default) =>
        _host.StartAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopServerAsync() => _host.StopAsync();

    /// <inheritdoc />
    public Task<AuthConnectResult> ConnectAuthAsync(
        string username, string password, CancellationToken cancellationToken = default) =>
        _authLink.ConnectAsync(username, password, cancellationToken);

    /// <inheritdoc />
    public Task DisconnectAuthAsync() => _authLink.DisconnectAsync();

    /// <inheritdoc />
    public AuthCredentials GetAuthCredentials() =>
        new(_settings.AuthUsername, _settings.AuthPassword, _settings.AuthRemember);

    /// <inheritdoc />
    public async Task SaveAuthCredentialsAsync(
        AuthCredentials credentials, CancellationToken cancellationToken = default)
    {
        _settings.AuthUsername = credentials.Remember ? credentials.Username : string.Empty;
        _settings.AuthPassword = credentials.Remember
            ? LegacyHash.ToWireCredential(credentials.Password)
            : string.Empty;
        _settings.AuthPasswordFromExternalSecret = false;
        _settings.AuthRemember = credentials.Remember;
        await _store.SaveAsync(_settings, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task KickAsync(int userId, string reason) =>
        _moderation.KickAsync(userId, string.IsNullOrWhiteSpace(reason) ? "Kicked by staff" : reason);

    /// <inheritdoc />
    public async Task SetMutedAsync(int userId, bool muted)
    {
        if (muted)
        {
            await _moderation.MuteAsync(userId).ConfigureAwait(false);
        }
        else
        {
            await _moderation.UnmuteAsync(userId).ConfigureAwait(false);
        }
        PlayersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public Task BanAccountAsync(int userId, string reason, TimeSpan? duration) =>
        _moderation.BanAccountAsync(
            userId, string.IsNullOrWhiteSpace(reason) ? "Banned by staff" : reason, ConsoleActor, duration);

    /// <inheritdoc />
    public Task BanAddressAsync(string ipAddress, string reason, TimeSpan? duration) =>
        _moderation.BanAddressAsync(
            ipAddress, string.IsNullOrWhiteSpace(reason) ? "Banned by staff" : reason, ConsoleActor, duration);

    /// <inheritdoc />
    public Task DisconnectAsync(int userId) => _host.DisconnectUserAsync(userId);

    /// <inheritdoc />
    public bool RemoveBan(BanKind kind, string target) => _bans.Remove(kind, target);

    /// <inheritdoc />
    public async Task SetModeratorAsync(int userId, bool granted)
    {
        var sessionId = _users.FindSessionByUserId(userId);
        var user = sessionId is null ? null : _users.FindBySession(sessionId);
        if (user is null || user.IsModerator == granted)
        {
            return;
        }

        user.IsModerator = granted;
        await _host.SendToUserAsync(
            userId,
            NetworkMessage.Create(granted ? MessageType.ModeratorGranted : MessageType.ModeratorDenied))
            .ConfigureAwait(false);
        Record(ConsoleEventKind.Moderation,
            $"Console {(granted ? "granted" : "revoked")} moderator on player {userId}");
        PlayersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public Task BroadcastNoticeAsync(string text)
    {
        Record(ConsoleEventKind.Moderation, "Console broadcast a notice");
        return _host.BroadcastNoticeAsync(text);
    }

    /// <inheritdoc />
    public Task SendOocAsync(string text) => _host.SendOocAsync(text);

    /// <inheritdoc />
    public ServerConfig GetConfig() =>
        new(
            _settings.Name,
            _settings.ListenPort,
            _settings.IsPublic,
            _settings.HeartbeatSeconds,
            _settings.ModeratorPassword);

    /// <inheritdoc />
    public async Task ApplyConfigAsync(ServerConfig config, CancellationToken cancellationToken = default)
    {
        _settings.Name = config.Name;
        _settings.ListenPort = config.ListenPort;
        _settings.IsPublic = config.IsPublic;
        _settings.HeartbeatSeconds = config.HeartbeatSeconds;
        _settings.ModeratorPassword = config.ModeratorPassword;
        await _store.SaveAsync(_settings, cancellationToken).ConfigureAwait(false);
        Record(ConsoleEventKind.Info, "Configuration saved");
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAreas() => _settings.Areas.ToList();

    /// <inheritdoc />
    public IReadOnlyList<string> GetMusic() => _settings.Music.ToList();

    /// <inheritdoc />
    public IReadOnlyList<string> GetCharacters() => _settings.Characters.ToList();

    /// <inheritdoc />
    public IReadOnlyList<string> GetItems() => _settings.Items.ToList();

    /// <inheritdoc />
    public Task AddAreaAsync(string name, CancellationToken cancellationToken = default) =>
        EditListAsync(_settings.Areas, name, add: true, cancellationToken);

    /// <inheritdoc />
    public Task RemoveAreaAsync(string name, CancellationToken cancellationToken = default) =>
        EditListAsync(_settings.Areas, name, add: false, cancellationToken);

    /// <inheritdoc />
    public Task AddMusicAsync(string name, CancellationToken cancellationToken = default) =>
        EditListAsync(_settings.Music, name, add: true, cancellationToken);

    /// <inheritdoc />
    public Task RemoveMusicAsync(string name, CancellationToken cancellationToken = default) =>
        EditListAsync(_settings.Music, name, add: false, cancellationToken);

    /// <inheritdoc />
    public Task AddCharacterAsync(string name, CancellationToken cancellationToken = default) =>
        EditListAsync(_settings.Characters, name, add: true, cancellationToken);

    /// <inheritdoc />
    public Task RemoveCharacterAsync(string name, CancellationToken cancellationToken = default) =>
        EditListAsync(_settings.Characters, name, add: false, cancellationToken);

    /// <inheritdoc />
    public Task AddItemAsync(string name, CancellationToken cancellationToken = default) =>
        EditListAsync(_settings.Items, name, add: true, cancellationToken);

    /// <inheritdoc />
    public Task RemoveItemAsync(string name, CancellationToken cancellationToken = default) =>
        EditListAsync(_settings.Items, name, add: false, cancellationToken);

    private async Task EditListAsync(
        List<string> list, string name, bool add, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (add)
        {
            if (list.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }
            list.Add(trimmed);
        }
        else if (list.RemoveAll(x => string.Equals(x, trimmed, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return;
        }

        await _store.SaveAsync(_settings, cancellationToken).ConfigureAwait(false);
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    private string AreaNameOf(int areaId) =>
        areaId >= 0 && areaId < _settings.Areas.Count ? _settings.Areas[areaId] : $"Area {areaId}";

    private void OnStatusChanged(object? sender, ServerStatus status)
    {
        lock (_gate)
        {
            _startedAt = status == ServerStatus.Online ? DateTimeOffset.UtcNow : null;
        }
        StatusChanged?.Invoke(this, status);
    }

    private void OnUsersChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            _peakPlayers = Math.Max(_peakPlayers, _host.PlayerCount);
        }
        PlayersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnLogEntry(object? sender, string entry) => Record(ClassifyEvent(entry), entry);

    private void OnOocReceived(object? sender, OocLine line)
    {
        var entry = new ChatEntry(
            DateTimeOffset.Now, line.Sender, line.Text, line.Sender == "Server");
        lock (_gate)
        {
            _oocCount++;
            _ooc.Add(entry);
            Trim(_ooc);
        }
        OocReceived?.Invoke(this, entry);
    }

    private void OnIcReceived(object? sender, IcLine line)
    {
        var entry = new IcEntry(DateTimeOffset.Now, line.Character, line.Player, line.Text);
        lock (_gate)
        {
            _icCount++;
            _ic.Add(entry);
            Trim(_ic);
        }
        IcReceived?.Invoke(this, entry);
    }

    private void Record(ConsoleEventKind kind, string text)
    {
        var entry = new ConsoleEvent(DateTimeOffset.Now, kind, text);
        lock (_gate)
        {
            _events.Add(entry);
            Trim(_events);
        }
        EventLogged?.Invoke(this, entry);
    }

    // the host reports events as prose, sort the well known lines so the log can
    // color joins and staff actions without the host knowing about the console
    private static ConsoleEventKind ClassifyEvent(string entry)
    {
        // failures first, a rejected banned peer is a refusal not a staff action
        if (entry.Contains("Rejected", StringComparison.Ordinal) ||
            entry.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            return ConsoleEventKind.Error;
        }
        if (entry.Contains("connected", StringComparison.OrdinalIgnoreCase) ||
            entry.Contains("joined", StringComparison.OrdinalIgnoreCase))
        {
            return ConsoleEventKind.Join;
        }
        if (entry.StartsWith("Moderator ", StringComparison.Ordinal) ||
            entry.StartsWith("Staff ", StringComparison.Ordinal) ||
            entry.Contains("banned", StringComparison.OrdinalIgnoreCase) ||
            entry.Contains("kicked", StringComparison.OrdinalIgnoreCase) ||
            entry.Contains("muted", StringComparison.OrdinalIgnoreCase) ||
            entry.Contains("moderator", StringComparison.OrdinalIgnoreCase))
        {
            return ConsoleEventKind.Moderation;
        }
        return ConsoleEventKind.Info;
    }

    private static void Trim<T>(List<T> list)
    {
        while (list.Count > MaxHistory)
        {
            list.RemoveAt(0);
        }
    }
}
