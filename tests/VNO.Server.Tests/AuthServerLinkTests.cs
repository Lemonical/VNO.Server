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
/// Covers the auth server link resilience: the game server runs when the AS is
/// down and re-registers automatically once it comes back
/// </summary>
public sealed class AuthServerLinkTests
{
    private sealed class FakeClient : IMessageClient
    {
        public bool FailConnect { get; set; }
        public int ConnectAttempts { get; private set; }
        public List<NetworkMessage> Sent { get; } = new();
        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

#pragma warning disable CS0067
        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
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
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            State = ConnectionState.Disconnected;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static AuthServerLink Build(FakeClient client) =>
        new(client, Options.Create(new ServerSettings { Name = "Test", IsPublic = true }),
            NullLogger<AuthServerLink>.Instance);

    [Fact]
    public async Task Server_survives_the_AS_being_down_then_re_registers_when_it_returns()
    {
        var client = new FakeClient { FailConnect = true };
        await using var link = Build(client);

        // AS is down at startup, connect must not throw and nothing is registered
        await link.ConnectAsync();
        Assert.Equal(ConnectionState.Disconnected, link.State);
        Assert.DoesNotContain(client.Sent, m => m.Type == MessageType.Hello);

        // the AS comes back, the next maintenance tick reconnects and re-registers
        client.FailConnect = false;
        await link.MaintainAsync();
        Assert.Equal(ConnectionState.Connected, link.State);
        Assert.Contains(client.Sent, m => m.Type == MessageType.Hello);

        // once connected, maintenance sends heartbeats rather than reconnecting
        var attemptsBefore = client.ConnectAttempts;
        await link.MaintainAsync();
        Assert.Equal(attemptsBefore, client.ConnectAttempts);
        Assert.Contains(client.Sent, m => m.Type == MessageType.Heartbeat);
    }
}
