using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VNO.Core.Models;
using VNO.Core.Networking;
using VNO.Core.Protocol;

namespace VNO.Server.Services;

/// <summary>
/// Default game host built on the core message server
/// </summary>
/// <remarks>
/// Ports the listening and relay behavior of the legacy Form3. It wires the
/// session lifecycle to the user registry, enforces bans on connect, and routes
/// known messages such as in character chat and music to the rest of the players
/// </remarks>
public sealed class GameHost : IGameHost
{
    private readonly IMessageServer _server;
    private readonly IUserRegistry _users;
    private readonly IBanRegistry _bans;
    private readonly ServerSettings _settings;
    private readonly ILogger<GameHost> _logger;

    // areas a moderator has locked, non staff players cannot enter these
    private readonly HashSet<int> _lockedAreas = new();

    private ServerStatus _status = ServerStatus.Offline;

    /// <summary>
    /// Creates the host with its dependencies
    /// </summary>
    public GameHost(
        IMessageServer server,
        IUserRegistry users,
        IBanRegistry bans,
        IOptions<ServerSettings> settings,
        ILogger<GameHost> logger)
    {
        _server = server;
        _users = users;
        _bans = bans;
        _settings = settings.Value;
        _logger = logger;

        _server.SessionConnected += OnSessionConnected;
        _server.SessionDisconnected += OnSessionDisconnected;
        _server.MessageReceived += OnMessageReceived;
    }

    /// <inheritdoc />
    public ServerStatus Status => _status;

    /// <inheritdoc />
    public int PlayerCount => _server.SessionCount;

    /// <inheritdoc />
    public event EventHandler<ServerStatus>? StatusChanged;

    /// <inheritdoc />
    public event EventHandler? UsersChanged;

    /// <inheritdoc />
    public event EventHandler<string>? LogEntry;

    /// <inheritdoc />
    public event EventHandler<OocLine>? OocReceived;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_status == ServerStatus.Online)
        {
            return;
        }

        await _server.StartAsync(_settings.ListenPort, cancellationToken).ConfigureAwait(false);
        SetStatus(ServerStatus.Online);
        Log($"Server online on port {_settings.ListenPort}");
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        if (_status == ServerStatus.Offline)
        {
            return;
        }

        await _server.StopAsync().ConfigureAwait(false);
        SetStatus(ServerStatus.Offline);
        Log("Server offline");
        UsersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async Task SendToUserAsync(int userId, NetworkMessage message, CancellationToken cancellationToken = default)
    {
        var sessionId = _users.FindSessionByUserId(userId);
        if (sessionId is not null)
        {
            await _server.SendToAsync(sessionId, message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DisconnectUserAsync(int userId)
    {
        var sessionId = _users.FindSessionByUserId(userId);
        if (sessionId is not null)
        {
            await _server.DisconnectAsync(sessionId).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task BroadcastNoticeAsync(string text, CancellationToken cancellationToken = default) =>
        _server.BroadcastAsync(new NetworkMessage(MessageType.Notice, text), cancellationToken);

    /// <inheritdoc />
    public Task SendOocAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.CompletedTask;
        }

        // clients show the OOC text verbatim, so a server prefix marks the author
        var line = $"[Server] {text}";
        OocReceived?.Invoke(this, new OocLine("Server", text));
        return _server.BroadcastAsync(new NetworkMessage(MessageType.OutOfCharacter, line), cancellationToken);
    }

    private void HandleOutOfCharacter(ChatUser user, NetworkMessage message)
    {
        // surface the line to the admin monitor with its author, then relay as normal
        var sender = string.IsNullOrWhiteSpace(user.Name) ? $"Player {user.Id}" : user.Name;
        OocReceived?.Invoke(this, new OocLine(sender, message.GetArgument(0)));
        RelayIfAllowed(user, message);
    }

    private void HandleInCharacter(ChatUser user, NetworkMessage message)
    {
        // badges are owned by the master and delivered to each client at login, so the
        // game host relays the line untouched, the client draws the badge by shown name
        RelayIfAllowed(user, message);
    }

    private void OnSessionConnected(object? sender, SessionEventArgs e)
    {
        // reject a banned address before it ever gets a user record
        if (_bans.IsAddressBanned(e.RemoteAddress))
        {
            Log($"Rejected banned address {e.RemoteAddress}");
            _ = _server.DisconnectAsync(e.SessionId);
            return;
        }

        var user = _users.Add(e.SessionId, e.RemoteAddress);
        Log($"Player {user.Id} connected from {e.RemoteAddress}");
        UsersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSessionDisconnected(object? sender, SessionEventArgs e)
    {
        var user = _users.Remove(e.SessionId);
        if (user is not null)
        {
            Log($"Player {user.Id} disconnected");
            UsersChanged?.Invoke(this, EventArgs.Empty);
            // release the character the player held so grids free the slot
            if (!string.IsNullOrEmpty(user.Character))
            {
                BroadcastTakenCharacters();
            }
            // tell the area the player left who remains
            _ = BroadcastAreaUsersAsync(user.AreaId);
        }
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        var user = _users.FindBySession(e.SessionId);
        if (user is null)
        {
            return;
        }

        switch (e.Message.Type)
        {
            case MessageType.Hello:
                HandleHello(user, e.Message);
                break;
            case MessageType.InCharacter:
                HandleInCharacter(user, e.Message);
                break;
            case MessageType.Music:
                RelayIfAllowed(user, e.Message);
                break;
            case MessageType.OutOfCharacter:
                HandleOutOfCharacter(user, e.Message);
                break;
            case MessageType.PickCharacter:
                HandlePickCharacter(user, e.Message);
                break;
            case MessageType.JoinArea:
                HandleJoinArea(user, e.Message);
                break;
            case MessageType.ModeratorAuth:
                HandleModeratorAuth(user, e.Message);
                break;
            case MessageType.StatChange:
            case MessageType.GiveItem:
                HandleAnimatorCommand(user, e.Message);
                break;
            case MessageType.Timer:
                HandleTimer(user, e.Message);
                break;
            case MessageType.StreamImage:
            case MessageType.StreamMusic:
                HandleStaffBroadcast(user, e.Message);
                break;
            case MessageType.Notice:
                HandleStaffNotice(user, e.Message);
                break;
            case MessageType.StaffLookup:
                HandleStaffLookup(user, e.Message);
                break;
            case MessageType.Kick:
            case MessageType.Mute:
            case MessageType.Unmute:
            case MessageType.Ban:
            case MessageType.BanIp:
            case MessageType.MassMute:
            case MessageType.MassUnmute:
            case MessageType.DjOn:
            case MessageType.DjOff:
            case MessageType.LockRoom:
            case MessageType.UnlockRoom:
            case MessageType.Isolate:
                HandleModeratorCommand(user, e.Message);
                break;
            case MessageType.Heartbeat:
                // nothing to do, the read itself proves the link is alive
                break;
            default:
                _logger.LogDebug("Unhandled message {Type} from player {Id}", e.Message.Type, user.Id);
                break;
        }
    }

    private void HandleHello(ChatUser user, NetworkMessage message)
    {
        // the first argument is the chosen display name when present
        var name = message.GetArgument(0);
        if (!string.IsNullOrWhiteSpace(name))
        {
            user.Name = name;
        }

        // hand the joining player the server's areas, music, and roster, the
        // legacy client received these lists right after connecting
        _ = SendJoinListsAsync(user);

        // the player starts in the first area, tell that area who is present
        user.AreaId = 0;
        _ = BroadcastAreaUsersAsync(0);

        UsersChanged?.Invoke(this, EventArgs.Empty);
        _ = _server.BroadcastAsync(new NetworkMessage(MessageType.Notice, $"{user.Name} joined"));
    }

    private void HandleJoinArea(ChatUser user, NetworkMessage message)
    {
        if (!int.TryParse(
                message.GetArgument(0), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var area) || area < 0)
        {
            return;
        }
        if (area >= Math.Max(1, _settings.Areas.Count))
        {
            return;
        }

        var previous = user.AreaId;
        if (previous == area)
        {
            return;
        }

        // a moderator locked area is closed to non staff players
        if (_lockedAreas.Contains(area) && !user.IsModerator)
        {
            _ = SendToUserAsync(user.Id, new NetworkMessage(MessageType.Notice, "That area is locked"));
            return;
        }

        user.AreaId = area;
        UsersChanged?.Invoke(this, EventArgs.Empty);
        // refresh both the area the player left and the one they joined
        _ = BroadcastAreaUsersAsync(previous);
        _ = BroadcastAreaUsersAsync(area);
    }

    private async Task BroadcastAreaUsersAsync(int areaId)
    {
        var members = _users.Users.Where(u => u.AreaId == areaId).ToList();
        var names = members.Select(u => string.IsNullOrEmpty(u.Character) ? u.Name : u.Character).ToArray();
        var list = new NetworkMessage(MessageType.UserList, names);
        foreach (var member in members)
        {
            var sessionId = _users.FindSessionByUserId(member.Id);
            if (sessionId is not null)
            {
                await _server.SendToAsync(sessionId, list).ConfigureAwait(false);
            }
        }
    }

    private async Task SendJoinListsAsync(ChatUser user)
    {
        var sessionId = _users.FindSessionByUserId(user.Id);
        if (sessionId is null)
        {
            return;
        }

        if (_settings.Areas.Count > 0)
        {
            await _server.SendToAsync(sessionId, new NetworkMessage(MessageType.AreaList, _settings.Areas.ToArray()))
                .ConfigureAwait(false);
        }
        if (_settings.Music.Count > 0)
        {
            await _server.SendToAsync(sessionId, new NetworkMessage(MessageType.MusicList, _settings.Music.ToArray()))
                .ConfigureAwait(false);
        }
        if (_settings.Characters.Count > 0)
        {
            await _server.SendToAsync(
                sessionId, new NetworkMessage(MessageType.CharacterList, _settings.Characters.ToArray()))
                .ConfigureAwait(false);
        }
    }

    private void HandlePickCharacter(ChatUser user, NetworkMessage message)
    {
        var pick = message.GetArgument(0);
        if (string.IsNullOrWhiteSpace(pick))
        {
            return;
        }

        // reject a character another player already holds, the legacy taken check
        var takenByOther = _users.Users.Any(u =>
            u.Id != user.Id && string.Equals(u.Character, pick, StringComparison.OrdinalIgnoreCase));
        if (takenByOther)
        {
            _ = _server.SendToAsync(
                _users.FindSessionByUserId(user.Id)!, new NetworkMessage(MessageType.CharacterTaken, pick));
            return;
        }

        user.Character = pick;
        UsersChanged?.Invoke(this, EventArgs.Empty);
        BroadcastTakenCharacters();
    }

    private void BroadcastTakenCharacters()
    {
        var taken = _users.Users
            .Where(u => !string.IsNullOrEmpty(u.Character))
            .Select(u => u.Character)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _ = _server.BroadcastAsync(new NetworkMessage(MessageType.CharacterTaken, taken));
    }

    private void HandleStaffLookup(ChatUser staff, NetworkMessage message)
    {
        // lookups reveal player details, so they are staff only. The result is
        // sent back only to the requesting moderator, never broadcast
        if (!staff.IsModerator)
        {
            Log($"Ignored StaffLookup from non staff player {staff.Id}");
            return;
        }
        var kind = message.GetArgument(0);
        var targetArg = message.GetArgument(1);
        var text = kind switch
        {
            "ip" => LookupUser(targetArg) is { } u
                ? $"IP for [{u.Id}] {u.Name}: {(string.IsNullOrEmpty(u.IpAddress) ? "unknown" : u.IpAddress)}"
                : "No such player",
            "user" => LookupUser(targetArg) is { } u
                ? $"[{u.Id}] {u.Name} as {(string.IsNullOrEmpty(u.Character) ? "no character" : u.Character)} in area {u.AreaId}"
                : "No such player",
            "char" => FormatCharacterLookup(targetArg),
            "roomip" => FormatRoomIpLookup(staff.AreaId),
            _ => "Unknown lookup",
        };

        var sessionId = _users.FindSessionByUserId(staff.Id);
        if (sessionId is not null)
        {
            _ = _server.SendToAsync(sessionId, new NetworkMessage(MessageType.StaffLookupResult, text));
        }
        Log($"Staff {staff.Id} looked up {kind} {targetArg}");
    }

    private ChatUser? LookupUser(string idArg) =>
        int.TryParse(idArg, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var id)
            ? _users.Users.FirstOrDefault(u => u.Id == id)
            : null;

    private string FormatCharacterLookup(string character)
    {
        if (string.IsNullOrWhiteSpace(character))
        {
            return "Enter a character name";
        }
        var matches = _users.Users
            .Where(u => string.Equals(u.Character, character, StringComparison.OrdinalIgnoreCase))
            .Select(u => $"[{u.Id}] {u.Name}")
            .ToList();
        return matches.Count == 0
            ? $"No player is using {character}"
            : $"{character}: {string.Join(", ", matches)}";
    }

    private string FormatRoomIpLookup(int areaId)
    {
        var lines = _users.Users
            .Where(u => u.AreaId == areaId)
            .Select(u => $"[{u.Id}] {u.Name} {(string.IsNullOrEmpty(u.IpAddress) ? "unknown" : u.IpAddress)}")
            .ToList();
        return lines.Count == 0 ? "Area is empty" : string.Join("; ", lines);
    }

    private void HandleStaffNotice(ChatUser staff, NetworkMessage message)
    {
        // the animator server wide message is staff only, relayed to everyone as
        // a notice. A non staff sender is ignored so players cannot spoof notices
        if (!staff.IsModerator)
        {
            Log($"Ignored Notice from non staff player {staff.Id}");
            return;
        }
        var text = message.GetArgument(0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        _ = _server.BroadcastAsync(new NetworkMessage(MessageType.Notice, text));
        Log($"Staff {staff.Id} broadcast a server wide message");
    }

    private void HandleStaffBroadcast(ChatUser staff, NetworkMessage message)
    {
        // image and forced music streams are staff only, relayed to everyone
        if (!staff.IsModerator)
        {
            Log($"Ignored {message.Type} from non staff player {staff.Id}");
            return;
        }
        var url = message.GetArgument(0);
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }
        _ = _server.BroadcastAsync(message);
        Log($"Staff {staff.Id} broadcast {message.Type}");
    }

    private void HandleTimer(ChatUser staff, NetworkMessage message)
    {
        // starting a countdown is a staff action, the server relays it to all
        if (!staff.IsModerator)
        {
            Log($"Ignored Timer from non staff player {staff.Id}");
            return;
        }
        if (!int.TryParse(
                message.GetArgument(0), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
        {
            return;
        }
        _ = _server.BroadcastAsync(new NetworkMessage(
            MessageType.Timer, seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Log($"Staff {staff.Id} started a {seconds} second timer");
    }

    private void HandleAnimatorCommand(ChatUser staff, NetworkMessage message)
    {
        // the animator interface is staff only, like the moderator commands
        if (!staff.IsModerator)
        {
            Log($"Ignored {message.Type} from non staff player {staff.Id}");
            return;
        }

        // target "0" means the staff member edits their own stats, otherwise the
        // first argument is the target player id
        var targetArg = message.GetArgument(0);
        ChatUser? target;
        if (targetArg == "0")
        {
            target = staff;
        }
        else if (int.TryParse(
                     targetArg, System.Globalization.NumberStyles.Integer,
                     System.Globalization.CultureInfo.InvariantCulture, out var id))
        {
            target = _users.Users.FirstOrDefault(u => u.Id == id);
        }
        else
        {
            target = null;
        }

        if (target is null)
        {
            return;
        }

        // deliver the change to the target so their client applies it
        var sessionId = _users.FindSessionByUserId(target.Id);
        if (sessionId is not null)
        {
            _ = _server.SendToAsync(sessionId, message);
        }
        Log($"Staff {staff.Id} sent {message.Type} to player {target.Id}");
    }

    private void HandleModeratorAuth(ChatUser user, NetworkMessage message)
    {
        var sessionId = _users.FindSessionByUserId(user.Id);
        if (sessionId is null)
        {
            return;
        }

        // an empty configured password disables in game moderator auth, and a
        // blank submission never grants, matching the legacy Wrong Password path
        var password = message.GetArgument(0);
        var ok = !string.IsNullOrEmpty(_settings.ModeratorPassword) &&
            string.Equals(password, _settings.ModeratorPassword, StringComparison.Ordinal);

        if (ok)
        {
            user.IsModerator = true;
            Log($"Player {user.Id} authenticated as moderator");
            _ = _server.SendToAsync(sessionId, NetworkMessage.Create(MessageType.ModeratorGranted));
            UsersChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _ = _server.SendToAsync(sessionId, NetworkMessage.Create(MessageType.ModeratorDenied));
        }
    }

    private void HandleModeratorCommand(ChatUser moderator, NetworkMessage message)
    {
        // only staff may issue moderation commands, the legacy Form1 was gated by
        // the moderator login. A non moderator sender is silently ignored
        if (!moderator.IsModerator)
        {
            Log($"Ignored {message.Type} from non moderator player {moderator.Id}");
            return;
        }

        var reason = message.Type is MessageType.BanIp ? string.Empty : message.GetArgument(1);
        switch (message.Type)
        {
            case MessageType.Kick:
                ForEachTarget(message.GetArgument(0), target =>
                {
                    _ = SendToUserAsync(target.Id, new NetworkMessage(MessageType.Kick, reason));
                    _ = DisconnectUserAsync(target.Id);
                    Log($"Moderator {moderator.Id} kicked player {target.Id}");
                });
                break;

            case MessageType.Mute:
                ForEachTarget(message.GetArgument(0), target =>
                {
                    target.IsMuted = true;
                    _ = SendToUserAsync(target.Id, NetworkMessage.Create(MessageType.Mute));
                    Log($"Moderator {moderator.Id} muted player {target.Id}");
                });
                break;

            case MessageType.Unmute:
                ForEachTarget(message.GetArgument(0), target =>
                {
                    target.IsMuted = false;
                    _ = SendToUserAsync(target.Id, NetworkMessage.Create(MessageType.Unmute));
                    Log($"Moderator {moderator.Id} unmuted player {target.Id}");
                });
                break;

            case MessageType.Ban:
                ForEachTarget(message.GetArgument(0), target =>
                {
                    if (!string.IsNullOrEmpty(target.Name))
                    {
                        _bans.Add(new BanEntry { Kind = BanKind.Account, Target = target.Name, Reason = reason });
                    }
                    _ = SendToUserAsync(target.Id, new NetworkMessage(MessageType.Ban, reason));
                    _ = DisconnectUserAsync(target.Id);
                    Log($"Moderator {moderator.Id} banned player {target.Id}");
                });
                break;

            case MessageType.BanIp:
                var address = message.GetArgument(0);
                if (!string.IsNullOrWhiteSpace(address))
                {
                    _bans.Add(new BanEntry { Kind = BanKind.IpAddress, Target = address, Reason = message.GetArgument(1) });
                    foreach (var target in _users.Users.Where(u => u.IpAddress == address).ToList())
                    {
                        _ = DisconnectUserAsync(target.Id);
                    }
                    Log($"Moderator {moderator.Id} banned address {address}");
                }
                break;

            case MessageType.MassMute:
                foreach (var target in _users.Users.Where(u =>
                             u.AreaId == moderator.AreaId && !u.IsModerator).ToList())
                {
                    target.IsMuted = true;
                    _ = SendToUserAsync(target.Id, NetworkMessage.Create(MessageType.Mute));
                }
                Log($"Moderator {moderator.Id} mass muted area {moderator.AreaId}");
                break;

            case MessageType.MassUnmute:
                foreach (var target in _users.Users.Where(u => u.AreaId == moderator.AreaId).ToList())
                {
                    target.IsMuted = false;
                    _ = SendToUserAsync(target.Id, NetworkMessage.Create(MessageType.Unmute));
                }
                Log($"Moderator {moderator.Id} mass unmuted area {moderator.AreaId}");
                break;

            case MessageType.DjOn:
                ForEachTarget(message.GetArgument(0), target =>
                {
                    target.IsDj = true;
                    Log($"Moderator {moderator.Id} granted DJ to player {target.Id}");
                });
                break;

            case MessageType.DjOff:
                ForEachTarget(message.GetArgument(0), target =>
                {
                    target.IsDj = false;
                    Log($"Moderator {moderator.Id} revoked DJ from player {target.Id}");
                });
                break;

            case MessageType.LockRoom:
                _lockedAreas.Add(moderator.AreaId);
                Log($"Moderator {moderator.Id} locked area {moderator.AreaId}");
                break;

            case MessageType.UnlockRoom:
                _lockedAreas.Remove(moderator.AreaId);
                Log($"Moderator {moderator.Id} unlocked area {moderator.AreaId}");
                break;

            case MessageType.Isolate:
                ForEachTarget(message.GetArgument(0), target =>
                {
                    target.IsIsolated = !target.IsIsolated;
                    Log($"Moderator {moderator.Id} set isolate={target.IsIsolated} on player {target.Id}");
                });
                break;
        }

        UsersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ForEachTarget(string userIdArg, Action<ChatUser> act)
    {
        if (!int.TryParse(
                userIdArg, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var targetId))
        {
            return;
        }
        var target = _users.Users.FirstOrDefault(u => u.Id == targetId);
        if (target is not null)
        {
            act(target);
        }
    }

    private void RelayIfAllowed(ChatUser user, NetworkMessage message)
    {
        if (user.IsMuted)
        {
            Log($"Dropped message from muted player {user.Id}");
            return;
        }

        // a player whose DJ permission was revoked cannot change the music
        if (message.Type is MessageType.Music && !user.IsDj)
        {
            Log($"Dropped music change from non DJ player {user.Id}");
            return;
        }

        // an isolated player's chat only echoes back to themselves, so the rest of
        // the area never hears them, the legacy ID Isolate action
        if (user.IsIsolated)
        {
            var sessionId = _users.FindSessionByUserId(user.Id);
            if (sessionId is not null)
            {
                _ = _server.SendToAsync(sessionId, message);
            }
            Log($"Echoed isolated player {user.Id} message back to themselves only");
            return;
        }

        _ = _server.BroadcastAsync(message);
    }

    private void SetStatus(ServerStatus status)
    {
        if (_status == status)
        {
            return;
        }

        _status = status;
        StatusChanged?.Invoke(this, status);
    }

    private void Log(string text)
    {
        _logger.LogInformation("{Entry}", text);
        LogEntry?.Invoke(this, text);
    }
}
