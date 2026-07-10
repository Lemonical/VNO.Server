using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using VNO.Core.Models;

namespace VNO.Server.Admin;

/// <summary>
/// Token-authenticated WebSocket backend for the detached React Ink server console
/// </summary>
public sealed class ServerAdminEndpoint : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly CommandInfo[] Commands =
    [
        new("help", "Show command help", "help"),
        new("status", "Show server and auth status", "status"),
        new("players", "List connected players", "players"),
        new("player", "Inspect one player", "player <id>"),
        new("kick", "Kick a player", "kick <id> [reason]"),
        new("mute", "Mute a player", "mute <id>"),
        new("unmute", "Unmute a player", "unmute <id>"),
        new("banip", "Ban an address", "banip <address> [reason]"),
        new("banaccount", "Ban a connected account", "banaccount <id> [reason]"),
        new("bans", "List active bans", "bans"),
        new("unban", "Remove a ban", "unban <account|address> <target>"),
        new("mod", "Grant or revoke moderator", "mod <grant|revoke> <id>"),
        new("notice", "Broadcast a notice", "notice <text>"),
        new("ooc", "Send OOC as Server", "ooc <text>"),
        new("start", "Start player listener", "start"),
        new("stop", "Stop player listener", "stop"),
        new("issues", "List warnings and errors", "issues"),
        new("history", "Show recent IC or OOC lines", "history <ic|ooc>"),
        new("config", "Show or update server configuration", "config [set <key> <value>]"),
        new("areas", "List/add/remove areas", "areas [add|remove] [name]"),
        new("music", "List/add/remove music", "music [add|remove] [name]"),
        new("chars", "List/add/remove characters", "chars [add|remove] [name]"),
        new("items", "List/add/remove item definitions", "items [add|remove] [name]"),
        new("clear", "Clear transcript", "clear"),
    ];

    private readonly IServerAdminController _admin;
    private WebApplication? _application;

    /// <summary>
    /// Creates the endpoint over the application admin controller
    /// </summary>
    public ServerAdminEndpoint(IServerAdminController admin)
    {
        _admin = admin;
        Port = ReadInteger("VNO_ADMIN_PORT", 6542);
        var dataDirectory = Environment.GetEnvironmentVariable("VNO_DATA_DIRECTORY") ??
            Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDirectory);
        TokenFile = Environment.GetEnvironmentVariable("VNO_ADMIN_TOKEN_FILE") ??
            Path.Combine(dataDirectory, "admin.token");
    }

    /// <summary>
    /// Listener port
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Bearer token accepted by the endpoint
    /// </summary>
    public string Token { get; private set; } = string.Empty;

    /// <summary>
    /// File containing the bearer token for detached clients
    /// </summary>
    public string TokenFile { get; }

    /// <summary>
    /// WebSocket URL suitable for a local console
    /// </summary>
    public string LocalUrl => $"ws://127.0.0.1:{Port}/admin";

    /// <summary>
    /// Configured server name shown by attached consoles
    /// </summary>
    public string Name => _admin.GetOverview().Name;

    /// <summary>
    /// Starts the endpoint once
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_application is not null)
        {
            return;
        }

        Token = LoadOrCreateToken(TokenFile);
        var bind = Environment.GetEnvironmentVariable("VNO_ADMIN_BIND") ?? "127.0.0.1";
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://{bind}:{Port}");
        var app = builder.Build();
        app.UseWebSockets();
        app.Map("/admin", HandleAsync);
        app.MapGet("/health", () => Results.Ok("ok"));
        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        _application = app;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_application is null)
        {
            return;
        }

        await _application.StopAsync().ConfigureAwait(false);
        await _application.DisposeAsync().ConfigureAwait(false);
        _application = null;
    }

    private async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest || !Authorized(context.Request.Headers.Authorization))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        using var sendGate = new SemaphoreSlim(1, 1);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        EventHandler<ConsoleEvent> live = (_, entry) =>
            _ = SendAsync(socket, new { type = "line", text = $"[{entry.Timestamp:HH:mm:ss}] {entry.Text}" }, sendGate, cancellation.Token);
        EventHandler<ServerStatus> statusChanged = (_, _) =>
            _ = SendStatusSafelyAsync(socket, sendGate, cancellation.Token);
        EventHandler<ConnectionState> authChanged = (_, _) =>
            _ = SendStatusSafelyAsync(socket, sendGate, cancellation.Token);
        EventHandler playersChanged = (_, _) =>
            _ = SendStatusSafelyAsync(socket, sendGate, cancellation.Token);
        EventHandler configChanged = (_, _) =>
            _ = SendStatusSafelyAsync(socket, sendGate, cancellation.Token);
        _admin.EventLogged += live;
        _admin.StatusChanged += statusChanged;
        _admin.AuthStateChanged += authChanged;
        _admin.PlayersChanged += playersChanged;
        _admin.ConfigChanged += configChanged;
        try
        {
            await SendAsync(socket, new
            {
                type = "welcome",
                name = Name,
                protocolVersion = "1",
                commands = Commands.Select(command => new
                {
                    name = command.Name,
                    aliases = Array.Empty<string>(),
                    summary = command.Summary,
                    usage = command.Usage,
                }),
            }, sendGate, cancellation.Token).ConfigureAwait(false);
            await SendStatusAsync(socket, sendGate, cancellation.Token).ConfigureAwait(false);
            await ReceiveAsync(socket, sendGate, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            _admin.EventLogged -= live;
            _admin.StatusChanged -= statusChanged;
            _admin.AuthStateChanged -= authChanged;
            _admin.PlayersChanged -= playersChanged;
            _admin.ConfigChanged -= configChanged;
            cancellation.Cancel();
        }
    }

    private async Task SendStatusSafelyAsync(
        WebSocket socket,
        SemaphoreSlim sendGate,
        CancellationToken cancellationToken)
    {
        try
        {
            await SendStatusAsync(socket, sendGate, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            // The console detached while a controller event was being delivered.
        }
    }

    private async Task ReceiveAsync(
        WebSocket socket,
        SemaphoreSlim sendGate,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }
            if (!result.EndOfMessage)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.MessageTooBig,
                    "Admin request too large",
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            AdminRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<AdminRequest>(
                    buffer.AsSpan(0, result.Count),
                    Json);
            }
            catch (JsonException)
            {
                await SendAsync(socket, new { type = "error", message = "Malformed request" }, sendGate, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            if (request is null)
            {
                continue;
            }

            if (request.Type.Equals("complete", StringComparison.OrdinalIgnoreCase))
            {
                var prefix = (request.Line ?? string.Empty).TrimStart();
                await SendAsync(socket, new
                {
                    type = "completions",
                    requestId = request.Id,
                    candidates = Commands
                        .Where(command => command.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .Select(command => new { value = command.Name, description = command.Summary }),
                }, sendGate, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (request.Type.Equals("command", StringComparison.OrdinalIgnoreCase))
            {
                await ExecuteAsync(socket, sendGate, request.Line ?? string.Empty, cancellationToken)
                    .ConfigureAwait(false);
                await SendStatusAsync(socket, sendGate, cancellationToken).ConfigureAwait(false);
                await SendAsync(socket, new
                {
                    type = "commandCompleted",
                    requestId = request.Id,
                    sessionEnded = false,
                }, sendGate, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ExecuteAsync(
        WebSocket socket,
        SemaphoreSlim sendGate,
        string line,
        CancellationToken cancellationToken)
    {
        var parts = line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        var verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
        var argument = parts.Length > 1 ? parts[1] : string.Empty;
        var remainder = parts.Length > 2 ? parts[2] : string.Empty;

        switch (verb)
        {
            case "":
                return;
            case "help":
                await TableAsync(socket, sendGate, "Commands", ["Command", "Description"],
                    Commands.Select(command => new[] { command.Usage, command.Summary }), cancellationToken).ConfigureAwait(false);
                return;
            case "status":
                await SendStatusAsync(socket, sendGate, cancellationToken).ConfigureAwait(false);
                return;
            case "players":
                await PlayersAsync(socket, sendGate, _admin.GetPlayers(), cancellationToken).ConfigureAwait(false);
                return;
            case "player" when TryId(argument, out var playerId):
                var player = _admin.GetPlayers().FirstOrDefault(candidate => candidate.Id == playerId);
                await PlayersAsync(socket, sendGate, player is null ? [] : [player], cancellationToken).ConfigureAwait(false);
                return;
            case "kick" when TryId(argument, out var kickId):
                await _admin.KickAsync(kickId, remainder).ConfigureAwait(false);
                break;
            case "mute" when TryId(argument, out var muteId):
                await _admin.SetMutedAsync(muteId, true).ConfigureAwait(false);
                break;
            case "unmute" when TryId(argument, out var unmuteId):
                await _admin.SetMutedAsync(unmuteId, false).ConfigureAwait(false);
                break;
            case "banip" when argument.Length > 0:
                await _admin.BanAddressAsync(argument, remainder, null).ConfigureAwait(false);
                break;
            case "banaccount" when TryId(argument, out var accountId):
                await _admin.BanAccountAsync(accountId, remainder, null).ConfigureAwait(false);
                break;
            case "bans":
                await TableAsync(socket, sendGate, "Active bans",
                    ["Kind", "Target", "Reason", "By", "Expires"],
                    _admin.GetBans().Select(ban => new[]
                    {
                        ban.Kind.ToString(),
                        ban.Target,
                        ban.Reason,
                        ban.PlacedBy,
                        ban.ExpiresAt?.ToString("u", CultureInfo.InvariantCulture) ?? "never",
                    }), cancellationToken).ConfigureAwait(false);
                return;
            case "unban" when parts.Length >= 3:
                var kind = argument.Equals("address", StringComparison.OrdinalIgnoreCase)
                    ? BanKind.IpAddress
                    : BanKind.Account;
                _admin.RemoveBan(kind, remainder);
                break;
            case "mod" when parts.Length >= 3 && TryId(remainder, out var modId):
                await _admin.SetModeratorAsync(
                    modId,
                    argument.Equals("grant", StringComparison.OrdinalIgnoreCase)).ConfigureAwait(false);
                break;
            case "notice" when argument.Length > 0:
                await _admin.BroadcastNoticeAsync(line[(verb.Length + 1)..]).ConfigureAwait(false);
                break;
            case "ooc" when argument.Length > 0:
                await _admin.SendOocAsync(line[(verb.Length + 1)..]).ConfigureAwait(false);
                break;
            case "start":
                await _admin.StartServerAsync(cancellationToken).ConfigureAwait(false);
                break;
            case "stop":
                await _admin.StopServerAsync().ConfigureAwait(false);
                break;
            case "issues":
                await TableAsync(socket, sendGate, "Issues", ["Time", "Level", "Message"],
                    _admin.GetIssues().Select(issue => new[]
                    {
                        issue.Timestamp.ToString("u", CultureInfo.InvariantCulture),
                        issue.Level.ToString(),
                        issue.Text,
                    }), cancellationToken).ConfigureAwait(false);
                return;
            case "history" when argument.Equals("ooc", StringComparison.OrdinalIgnoreCase):
                await TableAsync(socket, sendGate, "OOC history", ["Time", "Sender", "Text"],
                    _admin.GetOocHistory().Select(entry => new[]
                    {
                        entry.Timestamp.ToString("u", CultureInfo.InvariantCulture),
                        entry.Sender,
                        entry.Text,
                    }), cancellationToken).ConfigureAwait(false);
                return;
            case "history" when argument.Equals("ic", StringComparison.OrdinalIgnoreCase):
                await TableAsync(socket, sendGate, "IC history", ["Time", "Character", "Player", "Text"],
                    _admin.GetIcHistory().Select(entry => new[]
                    {
                        entry.Timestamp.ToString("u", CultureInfo.InvariantCulture),
                        entry.Character,
                        entry.Player,
                        entry.Text,
                    }), cancellationToken).ConfigureAwait(false);
                return;
            case "config":
                await ConfigAsync(socket, sendGate, argument, remainder, cancellationToken).ConfigureAwait(false);
                return;
            case "areas":
                await DefinitionsAsync(socket, sendGate, "Areas", argument, remainder,
                    _admin.GetAreas, _admin.AddAreaAsync, _admin.RemoveAreaAsync, cancellationToken).ConfigureAwait(false);
                return;
            case "music":
                await DefinitionsAsync(socket, sendGate, "Music", argument, remainder,
                    _admin.GetMusic, _admin.AddMusicAsync, _admin.RemoveMusicAsync, cancellationToken).ConfigureAwait(false);
                return;
            case "chars":
                await DefinitionsAsync(socket, sendGate, "Characters", argument, remainder,
                    _admin.GetCharacters, _admin.AddCharacterAsync, _admin.RemoveCharacterAsync, cancellationToken).ConfigureAwait(false);
                return;
            case "items":
                await DefinitionsAsync(socket, sendGate, "Items", argument, remainder,
                    _admin.GetItems, _admin.AddItemAsync, _admin.RemoveItemAsync, cancellationToken).ConfigureAwait(false);
                return;
            case "clear":
                await SendAsync(socket, new { type = "clear" }, sendGate, cancellationToken).ConfigureAwait(false);
                return;
            default:
                await SendAsync(socket, new { type = "error", message = "Unknown command or invalid arguments" },
                    sendGate, cancellationToken).ConfigureAwait(false);
                return;
        }

        await SendAsync(socket, new { type = "line", text = "OK" }, sendGate, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ConfigAsync(
        WebSocket socket,
        SemaphoreSlim sendGate,
        string action,
        string remainder,
        CancellationToken cancellationToken)
    {
        if (!action.Equals("set", StringComparison.OrdinalIgnoreCase))
        {
            var config = _admin.GetConfig();
            await TableAsync(socket, sendGate, "Configuration", ["Key", "Value"],
                new[]
                {
                    new[] { "name", config.Name },
                    new[] { "port", config.ListenPort.ToString(CultureInfo.InvariantCulture) },
                    new[] { "public", config.IsPublic ? "on" : "off" },
                    new[] { "heartbeat", config.HeartbeatSeconds.ToString(CultureInfo.InvariantCulture) },
                    new[] { "moderatorpassword", config.ModeratorPassword.Length > 0 ? "configured" : "disabled" },
                }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var split = remainder.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (split.Length != 2)
        {
            await SendAsync(socket, new { type = "error", message = "Usage: config set <key> <value>" },
                sendGate, cancellationToken).ConfigureAwait(false);
            return;
        }

        var current = _admin.GetConfig();
        var key = split[0].ToLowerInvariant();
        var value = split[1];
        ServerConfig? updated = key switch
        {
            "name" => current with { Name = value },
            "port" when TryInteger(value, out var port) => current with { ListenPort = port },
            "public" when TryBoolean(value, out var isPublic) => current with { IsPublic = isPublic },
            "heartbeat" when TryInteger(value, out var heartbeat) => current with { HeartbeatSeconds = heartbeat },
            _ => null,
        };
        if (updated is null)
        {
            await SendAsync(socket, new { type = "error", message = "Unknown key or invalid value" },
                sendGate, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _admin.ApplyConfigAsync(updated, cancellationToken).ConfigureAwait(false);
        await SendAsync(socket, new { type = "line", text = $"Updated {key}" }, sendGate, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool TryInteger(string value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && result > 0;

    private static bool TryBoolean(string value, out bool result)
    {
        if (value is "on" or "true" or "1" or "yes")
        {
            result = true;
            return true;
        }
        if (value is "off" or "false" or "0" or "no")
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    private async Task SendStatusAsync(
        WebSocket socket,
        SemaphoreSlim sendGate,
        CancellationToken cancellationToken)
    {
        var overview = _admin.GetOverview();
        await SendAsync(socket, new
        {
            type = "status",
            name = overview.Name,
            port = overview.ListenPort,
            running = overview.Status == ServerStatus.Online,
            sessions = overview.PlayerCount,
            servers = overview.PeakPlayers,
            uptimeSeconds = (long)overview.Uptime.TotalSeconds,
        }, sendGate, cancellationToken).ConfigureAwait(false);
    }

    private static Task PlayersAsync(
        WebSocket socket,
        SemaphoreSlim sendGate,
        IEnumerable<PlayerSnapshot> players,
        CancellationToken cancellationToken) =>
        TableAsync(socket, sendGate, "Connected players",
            ["ID", "Name", "Character", "Area", "IP", "Flags", "Connected"],
            players.Select(player => new[]
            {
                player.Id.ToString(CultureInfo.InvariantCulture),
                player.Name,
                player.Character,
                player.AreaName,
                player.IpAddress,
                $"{(player.IsModerator ? "mod " : string.Empty)}{(player.IsMuted ? "muted" : string.Empty)}".Trim(),
                player.ConnectedAt.ToString("u", CultureInfo.InvariantCulture),
            }), cancellationToken);

    private static async Task DefinitionsAsync(
        WebSocket socket,
        SemaphoreSlim sendGate,
        string title,
        string action,
        string value,
        Func<IReadOnlyList<string>> get,
        Func<string, CancellationToken, Task> add,
        Func<string, CancellationToken, Task> remove,
        CancellationToken cancellationToken)
    {
        if (action.Equals("add", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
        {
            await add(value, cancellationToken).ConfigureAwait(false);
        }
        else if (action.Equals("remove", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
        {
            await remove(value, cancellationToken).ConfigureAwait(false);
        }

        await TableAsync(socket, sendGate, title, ["Name"],
            get().Select(item => new[] { item }), cancellationToken).ConfigureAwait(false);
    }

    private static Task TableAsync(
        WebSocket socket,
        SemaphoreSlim sendGate,
        string title,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows,
        CancellationToken cancellationToken) =>
        SendAsync(socket, new { type = "table", title, headers, rows }, sendGate, cancellationToken);

    private static async Task SendAsync(
        WebSocket socket,
        object frame,
        SemaphoreSlim sendGate,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, Json);
        await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            sendGate.Release();
        }
    }

    private bool Authorized(string? authorization)
    {
        const string prefix = "Bearer ";
        if (authorization is null || !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var supplied = Encoding.UTF8.GetBytes(authorization[prefix.Length..]);
        var expected = Encoding.UTF8.GetBytes(Token);
        return supplied.Length == expected.Length &&
            CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private static string LoadOrCreateToken(string path)
    {
        var configured = Environment.GetEnvironmentVariable("VNO_ADMIN_TOKEN");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length > 0)
            {
                RestrictTokenFile(path);
                return existing;
            }
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var temporary = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            File.WriteAllText(temporary, token);
            RestrictTokenFile(temporary);
            try
            {
                File.Move(temporary, path, overwrite: false);
                return token;
            }
            catch (IOException) when (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Trim();
                if (existing.Length == 0)
                {
                    throw new InvalidDataException("The server admin token file is empty: " + path);
                }
                RestrictTokenFile(path);
                return existing;
            }
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void RestrictTokenFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static int ReadInteger(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static bool TryId(string value, out int id) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);

    private sealed record AdminRequest(string Type, int Id, string? Line);
    private sealed record CommandInfo(string Name, string Summary, string Usage);
}
