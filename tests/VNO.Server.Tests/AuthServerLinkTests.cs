using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VNO.Core.Models;
using VNO.Core.Networking;
using VNO.Core.Protocol;
using VNO.Server.Services;
using Xunit;

namespace VNO.Server.Tests;

/// <summary>
/// Covers the auth server link handshake and resilience: version check then
/// account login then the public listing, honest failure results, and the
/// automatic re-registration once a dropped AS returns
/// </summary>
public sealed class AuthServerLinkTests
{
    private sealed class FakeClient : IMessageClient
    {
        public bool FailConnect { get; set; }
        public bool DenyLogin { get; set; }
        public bool RejectVersion { get; set; }
        public int ConnectAttempts { get; private set; }
        public List<NetworkMessage> Sent { get; } = new();
        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
#pragma warning disable CS0067 // the link subscribes, the fake never raises it
        public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;
#pragma warning restore CS0067

        public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            ConnectAttempts++;
            if (FailConnect)
            {
                throw new InvalidOperationException("AS is down");
            }
            State = ConnectionState.Connected;
            return Task.CompletedTask;
        }

        public Task SendAsync(NetworkMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);

            // answer the handshake the way the master would
            switch (message.Type)
            {
                case MessageType.VersionCheck:
                    Reply(RejectVersion
                        ? NetworkMessage.Create(MessageType.VersionRejected)
                        : NetworkMessage.Create(MessageType.VersionAccepted));
                    break;
                case MessageType.MasterLogin:
                    Reply(DenyLogin
                        ? NetworkMessage.Create(MessageType.LoginDenied)
                        : NetworkMessage.Create(MessageType.LoginGranted));
                    break;
            }
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            State = ConnectionState.Disconnected;
            return Task.CompletedTask;
        }

        public void Drop() => State = ConnectionState.Disconnected;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private void Reply(NetworkMessage message) =>
            MessageReceived?.Invoke(this, new MessageReceivedEventArgs(string.Empty, message));
    }

    private static AuthServerLink Build(FakeClient client) =>
        new(client, Options.Create(new ServerSettings { Name = "Test", ListenPort = 16789, IsPublic = true }),
            NullLogger<AuthServerLink>.Instance);

    [Fact]
    public async Task A_successful_sign_in_runs_the_whole_handshake_and_registers_the_listing()
    {
        var client = new FakeClient();
        await using var link = Build(client);

        var result = await link.ConnectAsync("operator", "hunter2");

        Assert.Equal(AuthConnectResult.Granted, result);
        Assert.Equal(ConnectionState.Connected, link.State);
        Assert.Equal("operator", link.Username);
        Assert.Contains(client.Sent, m => m.Type == MessageType.VersionCheck);
        Assert.Contains(client.Sent, m => m.Type == MessageType.MasterLogin);
        Assert.Contains(client.Sent, m => m.Type == MessageType.RegisterServer);
    }

    [Fact]
    public async Task A_denied_login_reports_denied_and_drops_the_socket()
    {
        var client = new FakeClient { DenyLogin = true };
        await using var link = Build(client);

        var result = await link.ConnectAsync("operator", "wrong");

        Assert.Equal(AuthConnectResult.Denied, result);
        Assert.Equal(ConnectionState.Disconnected, link.State);
        Assert.Null(link.Username);
        Assert.DoesNotContain(client.Sent, m => m.Type == MessageType.RegisterServer);
    }

    [Fact]
    public async Task A_rejected_version_reports_the_rejection()
    {
        var client = new FakeClient { RejectVersion = true };
        await using var link = Build(client);

        var result = await link.ConnectAsync("operator", "hunter2");

        Assert.Equal(AuthConnectResult.VersionRejected, result);
        Assert.Equal(ConnectionState.Disconnected, link.State);
        Assert.DoesNotContain(client.Sent, m => m.Type == MessageType.MasterLogin);
    }

    [Fact]
    public async Task Server_survives_the_AS_being_down_then_re_registers_when_it_returns()
    {
        var client = new FakeClient { FailConnect = true };
        await using var link = Build(client);

        // AS is down at startup, connect reports it and nothing is registered
        var result = await link.ConnectAsync("operator", "hunter2");
        Assert.Equal(AuthConnectResult.Unreachable, result);
        Assert.Equal(ConnectionState.Disconnected, link.State);
        Assert.DoesNotContain(client.Sent, m => m.Type == MessageType.RegisterServer);

        // the AS comes back, the next maintenance tick reruns the handshake
        client.FailConnect = false;
        await link.MaintainAsync();
        Assert.Equal(ConnectionState.Connected, link.State);
        Assert.Equal("operator", link.Username);
        Assert.Contains(client.Sent, m => m.Type == MessageType.RegisterServer);

        // once connected, maintenance sends heartbeats rather than reconnecting
        var attemptsBefore = client.ConnectAttempts;
        await link.MaintainAsync();
        Assert.Equal(attemptsBefore, client.ConnectAttempts);
        Assert.Contains(client.Sent, m => m.Type == MessageType.MasterHeartbeat);
    }

    [Fact]
    public async Task A_dropped_link_re_registers_on_the_next_maintenance_tick()
    {
        var client = new FakeClient();
        await using var link = Build(client);
        await link.ConnectAsync("operator", "hunter2");

        client.Drop();
        client.Sent.Clear();
        await link.MaintainAsync();

        Assert.Equal(ConnectionState.Connected, link.State);
        Assert.Contains(client.Sent, m => m.Type == MessageType.MasterLogin);
        Assert.Contains(client.Sent, m => m.Type == MessageType.RegisterServer);
    }
}
