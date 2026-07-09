using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
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
    private readonly IAuthServerLink _auth;
    private readonly ServerSettings _settings;
    private readonly ILogger<GameHost> _logger;

    // areas a moderator has locked, non staff players cannot enter these
    private readonly ConcurrentDictionary<int, byte> _lockedAreas = new();

    // room policies a staff member toggles from the animator, off by default
    private bool _allowHide;
    private bool _hideRoomCount;
    private bool _selfHpEdit;

    // a light per player ledger so staff item and credit checks have a real answer,
    // keyed by the session scoped user id
    private readonly ConcurrentDictionary<int, int> _credits = new();
    private readonly ConcurrentDictionary<int, int> _items = new();

    // per player token bucket that caps chat spam before it is relayed to the area
    private readonly ChatFloodLimiter _chatFlood;
    private readonly ChatFloodLimiter _moderatorAuthFlood = new(3, 0.1);
    private readonly ConcurrentDictionary<string, PendingConnection> _pendingConnections =
        new(StringComparer.Ordinal);

    private ServerStatus _status = ServerStatus.Offline;

    /// <summary>
    /// Creates the host with its dependencies
    /// </summary>
    public GameHost(
        IMessageServer server,
        IUserRegistry users,
        IBanRegistry bans,
        IAuthServerLink auth,
        IOptions<ServerSettings> settings,
        ILogger<GameHost> logger)
    {
        _server = server;
        _users = users;
        _bans = bans;
        _auth = auth;
        _settings = settings.Value;
        _logger = logger;
        _chatFlood = new ChatFloodLimiter(_settings.ChatBurst, _settings.ChatMessagesPerSecond);

        _server.SessionConnected += OnSessionConnected;
        _server.SessionDisconnected += OnSessionDisconnected;
        _server.MessageReceived += OnMessageReceived;
    }

    /// <inheritdoc />
    public ServerStatus Status => _status;

    /// <inheritdoc />
    public int PlayerCount => _users.Users.Count;

    /// <inheritdoc />
    public event EventHandler<ServerStatus>? StatusChanged;

    /// <inheritdoc />
    public event EventHandler? UsersChanged;

    /// <inheritdoc />
    public event EventHandler<string>? LogEntry;

    /// <inheritdoc />
    public event EventHandler<OocLine>? OocReceived;

    /// <inheritdoc />
    public event EventHandler<IcLine>? IcReceived;

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
        // never trust the client asserted name in the first argument. The client draws the
        // master owned badge by the speaker's shown name, so a player could type a badge
        // holder's name and have that badge drawn for it. Stamp the identity the server knows
        // this player holds, the claimed character enforced by the taken check, so the shown
        // name and the badge resolved from it cannot be spoofed per message
        var identity = ResolveShownIdentity(user);
        var stamped = new NetworkMessage(MessageType.InCharacter, identity, message.GetArgument(1));
        var sender = string.IsNullOrWhiteSpace(user.Name) ? $"Player {user.Id}" : user.Name;
        IcReceived?.Invoke(this, new IcLine(identity, sender, message.GetArgument(1)));
        RelayIfAllowed(user, stamped);
    }

    // the server known identity for a player, the claimed character first, then the joined
    // name, then a stable fallback so an unnamed player still relays with a consistent label
    private static string ResolveShownIdentity(ChatUser user) =>
        !string.IsNullOrWhiteSpace(user.Character) ? user.Character
        : !string.IsNullOrWhiteSpace(user.Name) ? user.Name
        : $"Player {user.Id}";

    private void HandleRoomPolicy(ChatUser staff, NetworkMessage message)
    {
        if (!staff.IsModerator)
        {
            Log($"Ignored RoomPolicy from non staff player {staff.Id}");
            return;
        }

        var key = message.GetArgument(0).ToLowerInvariant();
        var on = ParseOnOff(message.GetArgument(1));
        switch (key)
        {
            case "allowhide":
                _allowHide = on;
                break;
            case "hideroomcount":
                _hideRoomCount = on;
                break;
            case "selfhpedit":
                _selfHpEdit = on;
                break;
            default:
                return;
        }

        // let clients know the policy so they can reflect it, hidden players stay hidden
        _ = _server.BroadcastAsync(new NetworkMessage(MessageType.RoomPolicy, key, on ? "on" : "off"));
        Log($"Staff {staff.Id} set policy {key} to {(on ? "on" : "off")}");
    }

    private void HandleHideSelf(ChatUser user, NetworkMessage message)
    {
        // hiding is only allowed when staff turned the policy on, staff can always hide
        if (!user.IsModerator && !_allowHide)
        {
            Log($"Ignored hide from player {user.Id}, hiding is not allowed");
            return;
        }

        var hidden = ParseOnOff(message.GetArgument(0));
        if (user.IsHidden == hidden)
        {
            return;
        }

        user.IsHidden = hidden;
        Log($"Player {user.Id} is now {(hidden ? "hidden" : "visible")}");
        UsersChanged?.Invoke(this, EventArgs.Empty);
        _ = BroadcastAreaUsersAsync(user.AreaId);
    }

    private void HandleGiveItem(ChatUser staff, NetworkMessage message)
    {
        if (!staff.IsModerator)
        {
            Log($"Ignored GiveItem from non staff player {staff.Id}");
            return;
        }

        var target = ResolveTarget(staff, message.GetArgument(0));
        if (target is null)
        {
            return;
        }

        // credits carry the literal "credits" tag, anything else is a plain item count
        if (string.Equals(message.GetArgument(1), "credits", StringComparison.OrdinalIgnoreCase))
        {
            _credits.AddOrUpdate(
                target.Id,
                ParseAmount(message.GetArgument(2)),
                (_, current) => current + ParseAmount(message.GetArgument(2)));
        }
        else
        {
            var itemName = message.GetArgument(1);
            if (!_settings.Items.Contains(itemName, StringComparer.OrdinalIgnoreCase))
            {
                Log($"Ignored undefined item '{itemName}' from staff {staff.Id}");
                return;
            }
            _items.AddOrUpdate(target.Id, 1, (_, current) => current + 1);
        }

        // still deliver to the target so their client reacts as before
        var sessionId = _users.FindSessionByUserId(target.Id);
        if (sessionId is not null)
        {
            _ = _server.SendToAsync(sessionId, message);
        }
        Log($"Staff {staff.Id} gave items to player {target.Id}");
    }

    private void HandleCheckInventory(ChatUser staff, NetworkMessage message)
    {
        if (!staff.IsModerator)
        {
            Log($"Ignored CheckInventory from non staff player {staff.Id}");
            return;
        }

        var target = ResolveTarget(staff, message.GetArgument(0));
        if (target is null)
        {
            return;
        }

        var kind = message.GetArgument(1).ToLowerInvariant();
        var result = kind == "credits"
            ? $"{target.Name}: {GetLedger(_credits, target.Id)} credits"
            : $"{target.Name}: {GetLedger(_items, target.Id)} items";

        var sessionId = _users.FindSessionByUserId(staff.Id);
        if (sessionId is not null)
        {
            _ = _server.SendToAsync(sessionId, new NetworkMessage(MessageType.StaffLookupResult, result));
        }
        Log($"Staff {staff.Id} checked {kind} for player {target.Id}");
    }

    private ChatUser? ResolveTarget(ChatUser staff, string targetArg) =>
        targetArg == "0"
            ? staff
            : int.TryParse(
                targetArg, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var id)
                ? _users.Users.FirstOrDefault(u => u.Id == id)
                : null;

    private static int GetLedger(ConcurrentDictionary<int, int> ledger, int id) =>
        ledger.TryGetValue(id, out var value) ? value : 0;

    private static int ParseAmount(string text) =>
        int.TryParse(
            text, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private static bool ParseOnOff(string text) =>
        text.Equals("on", StringComparison.OrdinalIgnoreCase) ||
        text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        text.Equals("1", StringComparison.Ordinal) ||
        text.Equals("yes", StringComparison.OrdinalIgnoreCase);

    private void OnSessionConnected(object? sender, SessionEventArgs e)
    {
        // reject a banned address before it ever gets a user record
        if (_bans.IsAddressBanned(e.RemoteAddress))
        {
            Log($"Rejected banned address {e.RemoteAddress}");
            _ = _server.DisconnectAsync(e.SessionId);
            return;
        }

        var pending = new PendingConnection(e.RemoteAddress);
        _pendingConnections[e.SessionId] = pending;
        _ = ExpirePendingConnectionAsync(e.SessionId, pending);
        _logger.LogDebug("Connection {SessionId} from {RemoteAddress} is awaiting Master authentication",
            e.SessionId, e.RemoteAddress);
    }

    private void OnSessionDisconnected(object? sender, SessionEventArgs e)
    {
        if (_pendingConnections.TryGetValue(e.SessionId, out var pending))
        {
            lock (pending.Gate)
            {
                if (_pendingConnections.TryRemove(e.SessionId, out var removed) &&
                    ReferenceEquals(removed, pending))
                {
                    pending.Disconnected = true;
                    pending.Cancellation.Cancel();
                    pending.Cancellation.Dispose();
                }
            }
        }

        var user = _users.Remove(e.SessionId);
        if (user is not null)
        {
            _credits.TryRemove(user.Id, out _);
            _items.TryRemove(user.Id, out _);
            _chatFlood.Forget(user.Id);
            _moderatorAuthFlood.Forget(user.Id);
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
            if (!_pendingConnections.TryGetValue(e.SessionId, out var pending))
            {
                return;
            }

            if (e.Message.Type == MessageType.VersionCheck)
            {
                _ = HandleVersionCheckAsync(e.SessionId, pending, e.Message);
            }
            else if (e.Message.Type == MessageType.Login)
            {
                _ = AuthenticateSafelyAsync(e.SessionId, e.Message.GetArgument(0));
            }
            else
            {
                _logger.LogDebug("Ignored pre-authentication {Type} from session {SessionId}",
                    e.Message.Type, e.SessionId);
            }
            return;
        }

        // chat carrying verbs are flood capped per player so one client cannot spam the area,
        // a dropped line is not relayed to anyone and never reaches the admin monitor
        if (e.Message.Type is MessageType.InCharacter or MessageType.OutOfCharacter or MessageType.Music
            && !_chatFlood.TryAcquire(user.Id))
        {
            _logger.LogDebug("Throttling {Type} flood from player {Id}", e.Message.Type, user.Id);
            return;
        }

        switch (e.Message.Type)
        {
            case MessageType.InCharacter:
                HandleInCharacter(user, e.Message);
                break;
            case MessageType.Music:
                HandleMusic(user, e.Message);
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
                HandleAnimatorCommand(user, e.Message);
                break;
            case MessageType.GiveItem:
                HandleGiveItem(user, e.Message);
                break;
            case MessageType.RoomPolicy:
                HandleRoomPolicy(user, e.Message);
                break;
            case MessageType.HideSelf:
                HandleHideSelf(user, e.Message);
                break;
            case MessageType.CheckInventory:
                HandleCheckInventory(user, e.Message);
                break;
            case MessageType.Timer:
                HandleTimer(user, e.Message);
                break;
            case MessageType.StreamImage:
            case MessageType.StreamMusic:
            case MessageType.SceneEffect:
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

    private async Task HandleVersionCheckAsync(
        string sessionId,
        PendingConnection pending,
        NetworkMessage message)
    {
        var accepted = message.Arguments.Count == 2 &&
            message.GetArgument(0).Equals("client", StringComparison.OrdinalIgnoreCase) &&
            message.GetArgument(1).Equals(ProtocolConstants.ClientVersion, StringComparison.Ordinal);

        lock (pending.Gate)
        {
            if (pending.Disconnected || pending.VersionChecked)
            {
                return;
            }
            pending.VersionChecked = true;
            pending.VersionAccepted = accepted;
        }

        await _server.SendToAsync(
            sessionId,
            NetworkMessage.Create(accepted ? MessageType.VersionAccepted : MessageType.VersionRejected))
            .ConfigureAwait(false);
        if (!accepted)
        {
            await _server.DisconnectAsync(sessionId).ConfigureAwait(false);
        }
    }

    private async Task AuthenticateSafelyAsync(string sessionId, string token)
    {
        try
        {
            await AuthenticateAsync(sessionId, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Game authentication failed for session {SessionId}", sessionId);
            await _server.DisconnectAsync(sessionId).ConfigureAwait(false);
        }
    }

    private async Task ExpirePendingConnectionAsync(string sessionId, PendingConnection pending)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), pending.Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (pending.Gate)
        {
            if (pending.Disconnected || !_pendingConnections.TryRemove(sessionId, out _))
            {
                return;
            }
            pending.Disconnected = true;
        }

        pending.Cancellation.Dispose();
        await RejectAuthenticationAsync(sessionId).ConfigureAwait(false);
    }

    private async Task AuthenticateAsync(string sessionId, string token)
    {
        if (!_pendingConnections.TryGetValue(sessionId, out var pending))
        {
            return;
        }

        lock (pending.Gate)
        {
            if (pending.Disconnected || !pending.VersionAccepted || pending.AuthenticationStarted)
            {
                return;
            }
            pending.AuthenticationStarted = true;
        }

        GameTokenValidationResult validation;
        try
        {
            validation = await _auth.ValidateGameTokenAsync(token, pending.Cancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Master validation failed for game session {SessionId}", sessionId);
            validation = GameTokenValidationResult.Invalid;
        }

        ChatUser? user = null;
        lock (pending.Gate)
        {
            if (!pending.Disconnected && validation.IsValid)
            {
                user = _users.Add(sessionId, pending.RemoteAddress);
                user.Name = validation.Username;
                user.AreaId = 0;
            }
            _pendingConnections.TryRemove(sessionId, out _);
            pending.Cancellation.Cancel();
            pending.Cancellation.Dispose();
        }

        if (user is null)
        {
            await RejectAuthenticationAsync(sessionId).ConfigureAwait(false);
            return;
        }

        Log($"Player {user.Id} authenticated as {user.Name} from {pending.RemoteAddress}");
        UsersChanged?.Invoke(this, EventArgs.Empty);

        // Authentication success and all server-owned definitions travel in one snapshot.
        await SendJoinListsAsync(user).ConfigureAwait(false);

        // the player starts in the first area, tell that area who is present
        await BroadcastAreaUsersAsync(0).ConfigureAwait(false);

        await _server.BroadcastAsync(new NetworkMessage(MessageType.Notice, $"{user.Name} joined"))
            .ConfigureAwait(false);
    }

    private async Task RejectAuthenticationAsync(string sessionId)
    {
        try
        {
            await _server.SendToAsync(sessionId, NetworkMessage.Create(MessageType.LoginRejected))
                .ConfigureAwait(false);
        }
        finally
        {
            await _server.DisconnectAsync(sessionId).ConfigureAwait(false);
        }
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
        if (_lockedAreas.ContainsKey(area) && !user.IsModerator)
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
        // hidden players are left out of the visible roster but still receive the list
        var names = members
            .Where(u => !u.IsHidden)
            .Select(u => string.IsNullOrEmpty(u.Character) ? u.Name : u.Character)
            .ToArray();
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

        var fields = new List<string>(
            4 + _settings.Areas.Count + _settings.Music.Count + _settings.Characters.Count + _settings.Items.Count)
        {
            _settings.Areas.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        fields.AddRange(_settings.Areas);
        fields.Add(_settings.Music.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        fields.AddRange(_settings.Music);
        fields.Add(_settings.Characters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        fields.AddRange(_settings.Characters);
        fields.Add(_settings.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        fields.AddRange(_settings.Items);

        await _server.SendToAsync(
            sessionId, new NetworkMessage(MessageType.JoinSnapshot, fields.ToArray())).ConfigureAwait(false);
    }

    private void HandlePickCharacter(ChatUser user, NetworkMessage message)
    {
        var pick = message.GetArgument(0);
        if (string.IsNullOrWhiteSpace(pick) ||
            !_settings.Characters.Contains(pick, StringComparer.OrdinalIgnoreCase))
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
        var pickerSession = _users.FindSessionByUserId(user.Id);
        if (pickerSession is not null)
        {
            _ = _server.SendToAsync(
                pickerSession,
                new NetworkMessage(MessageType.CharacterSelected, pick));
        }
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

    private void HandleMusic(ChatUser user, NetworkMessage message)
    {
        var track = message.GetArgument(0);
        if (!_settings.Music.Contains(track, StringComparer.OrdinalIgnoreCase))
        {
            Log($"Dropped undefined music track from player {user.Id}");
            return;
        }

        RelayIfAllowed(user, message);
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
        // target "0" means the sender edits their own stats, otherwise the first
        // argument is the target player id
        var targetArg = message.GetArgument(0);

        // the animator interface is staff only, except that a non staff player may
        // edit their own stats when the self HP edit policy is on
        var selfEdit = targetArg == "0" && _selfHpEdit;
        if (!staff.IsModerator && !selfEdit)
        {
            Log($"Ignored {message.Type} from non staff player {staff.Id}");
            return;
        }

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
        var ok = _moderatorAuthFlood.TryAcquire(user.Id) &&
            !string.IsNullOrEmpty(_settings.ModeratorPassword) &&
            FixedTimeCredentialEquals(
                LegacyHash.ToWireCredential(password),
                LegacyHash.ToWireCredential(_settings.ModeratorPassword));

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
                _lockedAreas.TryAdd(moderator.AreaId, 0);
                Log($"Moderator {moderator.Id} locked area {moderator.AreaId}");
                break;

            case MessageType.UnlockRoom:
                _lockedAreas.TryRemove(moderator.AreaId, out _);
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

        _ = RelayToAreaAsync(user.AreaId, message);
    }

    private static bool FixedTimeCredentialEquals(string supplied, string expected)
    {
        var suppliedBytes = Encoding.ASCII.GetBytes(supplied);
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        return suppliedBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }

    private async Task RelayToAreaAsync(int areaId, NetworkMessage message)
    {
        var recipients = _users.Users.Where(candidate => candidate.AreaId == areaId).ToList();
        foreach (var recipient in recipients)
        {
            var sessionId = _users.FindSessionByUserId(recipient.Id);
            if (sessionId is not null)
            {
                await _server.SendToAsync(sessionId, message).ConfigureAwait(false);
            }
        }
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

    private sealed class PendingConnection(string remoteAddress)
    {
        public object Gate { get; } = new();

        public string RemoteAddress { get; } = remoteAddress;

        public CancellationTokenSource Cancellation { get; } = new();

        public bool AuthenticationStarted { get; set; }

        public bool VersionAccepted { get; set; }

        public bool VersionChecked { get; set; }

        public bool Disconnected { get; set; }
    }
}
