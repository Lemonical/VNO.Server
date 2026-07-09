using System;
using System.Collections.Concurrent;
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
/// Exercises the join handshake over a real TCP loopback link, proving the game
/// host streams its definitions in one payload right after the hello
/// </summary>
public sealed class JoinStreamingTests
{
    [Fact]
    public async Task Pre_authentication_messages_create_no_player_or_game_state()
    {
        const int port = 47654;
        var users = new UserRegistry();
        var server = new TcpMessageServer(NullLogger<TcpMessageServer>.Instance);
        var host = new GameHost(
            server,
            users,
            new BanRegistry(),
            new FakeAuthLink(),
            Options.Create(new ServerSettings { ListenPort = port }),
            NullLogger<GameHost>.Instance);
        await host.StartAsync();

        await using var client = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        try
        {
            await client.ConnectAsync("127.0.0.1", port);
            await client.SendAsync(new NetworkMessage(MessageType.OutOfCharacter, "spoof"));
            await client.SendAsync(new NetworkMessage(MessageType.ModeratorAuth, "secret"));
            await client.SendAsync(new NetworkMessage(MessageType.PickCharacter, "Servant Archer"));
            await Task.Delay(150);

            Assert.Empty(users.Users);
            Assert.Equal(0, host.PlayerCount);
        }
        finally
        {
            await client.DisconnectAsync();
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Joining_player_receives_one_definition_snapshot()
    {
        const int port = 47653;
        var settings = Options.Create(new ServerSettings
        {
            ListenPort = port,
            Areas = new System.Collections.Generic.List<string> { "Courtroom", "Lobby" },
            Music = new System.Collections.Generic.List<string> { "Cornered.mp3", "Objection.mp3" },
        });

        var server = new TcpMessageServer(NullLogger<TcpMessageServer>.Instance);
        var host = new GameHost(server, new UserRegistry(), new BanRegistry(), new FakeAuthLink(), settings, NullLogger<GameHost>.Instance);
        await host.StartAsync();

        var received = new ConcurrentBag<NetworkMessage>();
        var gotSnapshot = new TaskCompletionSource();

        await using var client = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        client.MessageReceived += (_, e) =>
        {
            received.Add(e.Message);
            if (e.Message.Type == MessageType.JoinSnapshot)
            {
                gotSnapshot.TrySetResult();
            }
        };

        try
        {
            await client.ConnectAsync("127.0.0.1", port);
            await client.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await client.SendAsync(new NetworkMessage(MessageType.Login, "Tester"));

            await Task.WhenAny(gotSnapshot.Task, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.True(gotSnapshot.Task.IsCompleted, "did not receive the join snapshot");
            var snapshot = Assert.Single(received, message => message.Type == MessageType.JoinSnapshot);
            Assert.Equal(
                new[] { "2", "Courtroom", "Lobby", "2", "Cornered.mp3", "Objection.mp3", "1", "Servant Archer", "0" },
                snapshot.Arguments);
        }
        finally
        {
            await client.DisconnectAsync();
            await host.StopAsync();
        }
    }
}
