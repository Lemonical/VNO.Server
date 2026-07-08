using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VNO.Core.Models;
using VNO.Core.Networking;
using VNO.Core.Protocol;

namespace VNO.Server.Services;

/// <summary>
/// Default auth server link built on the core message client
/// </summary>
/// <remarks>
/// Speaks the master handshake, VersionCheck as role server, then MasterLogin
/// with the operator account, then RegisterServer when the listing is public.
/// A denied login drops the socket so the state never claims connected for a
/// session the master refused
/// </remarks>
public sealed class AuthServerLink : IAuthServerLink, IAsyncDisposable
{
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(10);

    private readonly IMessageClient _client;
    private readonly ServerSettings _settings;
    private readonly ILogger<AuthServerLink> _logger;

    private Timer? _heartbeatTimer;
    private TaskCompletionSource<bool>? _pendingVersion;
    private TaskCompletionSource<AuthConnectResult>? _pendingLogin;
    private string? _accountName;
    private string? _accountPassword;

    /// <summary>
    /// Creates the link with its dependencies
    /// </summary>
    public AuthServerLink(
        IMessageClient client,
        IOptions<ServerSettings> settings,
        ILogger<AuthServerLink> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
        _client.StateChanged += (_, e) => StateChanged?.Invoke(this, e.State);
        _client.MessageReceived += (_, e) => HandleMessage(e.Message);
    }

    /// <inheritdoc />
    public ConnectionState State => _client.State;

    /// <inheritdoc />
    public string? Username { get; private set; }

    /// <inheritdoc />
    public event EventHandler<ConnectionState>? StateChanged;

    /// <inheritdoc />
    public async Task<AuthConnectResult> ConnectAsync(
        string username, string password, CancellationToken cancellationToken = default)
    {
        _accountName = username;
        // the legacy server ran the password through MD5 before the CO command,
        // the master only ever sees the digest, so hash exactly like the client
        _accountPassword = LegacyHash.Md5Hex(password);

        var result = await HandshakeAsync(cancellationToken).ConfigureAwait(false);
        if (result == AuthConnectResult.Granted)
        {
            StartMaintenance();
        }
        else if (IsRefusal(result))
        {
            // the master looked at these credentials and said no, retrying with
            // the same ones would only trip its rate limiter
            _accountName = null;
            _accountPassword = null;
        }
        return result;
    }

    /// <summary>
    /// One maintenance step: heartbeat while connected, otherwise redo the whole
    /// handshake with the stored account so the server re-lists automatically
    /// once the AS returns. Runs on the timer and is called directly by tests
    /// </summary>
    public async Task MaintainAsync()
    {
        if (_client.State == ConnectionState.Connected)
        {
            try
            {
                await _client.SendAsync(NetworkMessage.Create(MessageType.MasterHeartbeat))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat to the auth server failed");
            }
            return;
        }

        if (_accountName is null || _accountPassword is null)
        {
            return;
        }

        var result = await HandshakeAsync(CancellationToken.None).ConfigureAwait(false);
        if (IsRefusal(result))
        {
            // the account stopped being valid mid run, stop hammering the master
            _accountName = null;
            _accountPassword = null;
            await StopMaintenanceAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        _accountName = null;
        _accountPassword = null;
        Username = null;
        await StopMaintenanceAsync().ConfigureAwait(false);
        await _client.DisconnectAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<AuthConnectResult> HandshakeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.ConnectAsync(_settings.AuthServerHost, _settings.AuthServerPort, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not reach the auth server");
            return AuthConnectResult.Unreachable;
        }

        try
        {
            // state our role and version first, the master refuses everything else
            var version = NewReply<bool>(out _pendingVersion);
            await _client.SendAsync(
                new NetworkMessage(MessageType.VersionCheck, "server", ProtocolConstants.ClientVersion),
                cancellationToken).ConfigureAwait(false);
            var accepted = await AwaitReplyAsync(version, cancellationToken).ConfigureAwait(false);
            if (accepted is null)
            {
                await DropAsync().ConfigureAwait(false);
                return AuthConnectResult.TimedOut;
            }
            if (!accepted.Value)
            {
                await DropAsync().ConfigureAwait(false);
                return AuthConnectResult.VersionRejected;
            }

            // hosting requires an account, sign in before asking for anything
            var login = NewReply<AuthConnectResult>(out _pendingLogin);
            await _client.SendAsync(
                new NetworkMessage(MessageType.MasterLogin, _accountName ?? string.Empty, _accountPassword ?? string.Empty),
                cancellationToken).ConfigureAwait(false);
            var outcome = await AwaitReplyAsync(login, cancellationToken).ConfigureAwait(false);
            if (outcome is null)
            {
                await DropAsync().ConfigureAwait(false);
                return AuthConnectResult.TimedOut;
            }
            if (outcome.Value != AuthConnectResult.Granted)
            {
                await DropAsync().ConfigureAwait(false);
                return outcome.Value;
            }

            Username = _accountName;

            // announce the listing only for a public server, the master publishes
            // the address it observed with the name and port we state here
            if (_settings.IsPublic)
            {
                await _client.SendAsync(
                    new NetworkMessage(
                        MessageType.RegisterServer,
                        _settings.Name,
                        _settings.ListenPort.ToString(CultureInfo.InvariantCulture)),
                    cancellationToken).ConfigureAwait(false);
            }

            // nudge listeners so a status bar picks up the signed in name
            StateChanged?.Invoke(this, _client.State);
            return AuthConnectResult.Granted;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The auth server handshake failed");
            await DropAsync().ConfigureAwait(false);
            return AuthConnectResult.Unreachable;
        }
    }

    private void HandleMessage(NetworkMessage message)
    {
        switch (message.Type)
        {
            case MessageType.VersionAccepted:
                _pendingVersion?.TrySetResult(true);
                break;

            case MessageType.VersionRejected:
            case MessageType.AddressBanned:
                _pendingVersion?.TrySetResult(false);
                break;

            case MessageType.LoginGranted:
                _pendingLogin?.TrySetResult(AuthConnectResult.Granted);
                break;

            case MessageType.LoginDenied:
                _pendingLogin?.TrySetResult(AuthConnectResult.Denied);
                break;

            case MessageType.AccountBanned:
                _pendingLogin?.TrySetResult(AuthConnectResult.Banned);
                break;
        }
    }

    private static TaskCompletionSource<T> NewReply<T>(out TaskCompletionSource<T> slot) =>
        slot = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<T?> AwaitReplyAsync<T>(
        TaskCompletionSource<T> pending, CancellationToken cancellationToken) where T : struct
    {
        var completed = await Task.WhenAny(pending.Task, Task.Delay(ReplyTimeout, cancellationToken))
            .ConfigureAwait(false);
        return completed == pending.Task ? await pending.Task.ConfigureAwait(false) : null;
    }

    // a refusal is the master rejecting the peer, not the network failing
    private static bool IsRefusal(AuthConnectResult result) =>
        result is AuthConnectResult.Denied or AuthConnectResult.Banned or AuthConnectResult.VersionRejected;

    private async Task DropAsync()
    {
        Username = null;
        try
        {
            await _client.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Closing the failed auth link threw");
        }
    }

    private void StartMaintenance()
    {
        _heartbeatTimer?.Dispose();
        var period = TimeSpan.FromSeconds(Math.Max(1, _settings.HeartbeatSeconds));
        _heartbeatTimer = new Timer(_ => _ = MaintainAsync(), null, period, period);
    }

    private async Task StopMaintenanceAsync()
    {
        if (_heartbeatTimer is not null)
        {
            await _heartbeatTimer.DisposeAsync().ConfigureAwait(false);
            _heartbeatTimer = null;
        }
    }
}
