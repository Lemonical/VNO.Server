using System;
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
public sealed class AuthServerLink : IAuthServerLink, IAsyncDisposable
{
    private readonly IMessageClient _client;
    private readonly ServerSettings _settings;
    private readonly ILogger<AuthServerLink> _logger;

    private Timer? _heartbeatTimer;

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
    }

    /// <inheritdoc />
    public ConnectionState State => _client.State;

    /// <inheritdoc />
    public event EventHandler<ConnectionState>? StateChanged;

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // try once now, then start the maintenance timer regardless. If the AS is
        // down the game server still runs, and the timer keeps retrying so the
        // server re-lists automatically once the AS returns, the legacy
        // "run anyway, only use the AS when it is up" behavior
        await TryConnectAndRegisterAsync(cancellationToken).ConfigureAwait(false);
        StartMaintenance();
    }

    private async Task TryConnectAndRegisterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.ConnectAsync(_settings.AuthServerHost, _settings.AuthServerPort, cancellationToken)
                .ConfigureAwait(false);

            // announce this server to the AS so it can be listed
            await _client.SendAsync(
                new NetworkMessage(MessageType.Hello, _settings.Name, _settings.IsPublic ? "1" : "0"),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not reach the auth server, will run without listing");
        }
    }

    /// <summary>
    /// One maintenance step: heartbeat while connected, otherwise try to reconnect
    /// and re-register. Runs on the timer and is called directly by tests
    /// </summary>
    public async Task MaintainAsync()
    {
        if (_client.State == ConnectionState.Connected)
        {
            await _client.SendAsync(NetworkMessage.Create(MessageType.Heartbeat)).ConfigureAwait(false);
        }
        else
        {
            await TryConnectAndRegisterAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        if (_heartbeatTimer is not null)
        {
            await _heartbeatTimer.DisposeAsync().ConfigureAwait(false);
            _heartbeatTimer = null;
        }

        await _client.DisconnectAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    private void StartMaintenance()
    {
        var period = TimeSpan.FromSeconds(Math.Max(1, _settings.HeartbeatSeconds));
        _heartbeatTimer = new Timer(_ => _ = MaintainAsync(), null, period, period);
    }
}
