using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VNO.Core.Networking;
using VNO.Core.Protocol;
using VNO.Server.Services;
using Xunit;

namespace VNO.Server.Tests;

/// <summary>
/// Exercises the server side OOC monitor, the read event and the two way send
/// </summary>
public sealed class OocMonitorTests
{
    private static async Task<bool> WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 60; i++)
        {
            if (condition())
            {
                return true;
            }
            await Task.Delay(50);
        }
        return condition();
    }

    private static async Task<(TcpMessageServer, GameHost, UserRegistry)> StartAsync(int port)
    {
        var settings = Options.Create(new ServerSettings { ListenPort = port });
        var users = new UserRegistry();
        var server = new TcpMessageServer(NullLogger<TcpMessageServer>.Instance);
        var host = new GameHost(server, users, new BanRegistry(), new FakeAuthLink(), settings, NullLogger<GameHost>.Instance);
        await host.StartAsync();
        return (server, host, users);
    }

    [Fact]
    public async Task Player_ooc_is_surfaced_to_the_monitor_with_its_author()
    {
        const int port = 47710;
        var (_, host, users) = await StartAsync(port);

        var seen = new ConcurrentQueue<OocLine>();
        host.OocReceived += (_, line) => seen.Enqueue(line);

        await using var player = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        try
        {
            await player.ConnectAsync("127.0.0.1", port);
            await player.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ApplicationVersion));
            await player.SendAsync(new NetworkMessage(MessageType.Login, "Phoenix"));
            Assert.True(await WaitAsync(() => users.Users.Any(u => u.Name == "Phoenix")));

            await player.SendAsync(new NetworkMessage(MessageType.OutOfCharacter, "objection"));

            Assert.True(await WaitAsync(() => seen.Any(l => l.Sender == "Phoenix" && l.Text == "objection")));
        }
        finally
        {
            await player.DisconnectAsync();
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Server_ooc_reaches_players_and_the_monitor()
    {
        const int port = 47711;
        var (_, host, users) = await StartAsync(port);

        var seen = new ConcurrentQueue<OocLine>();
        host.OocReceived += (_, line) => seen.Enqueue(line);

        var received = new ConcurrentQueue<NetworkMessage>();
        await using var player = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        player.MessageReceived += (_, e) => received.Enqueue(e.Message);
        try
        {
            await player.ConnectAsync("127.0.0.1", port);
            await player.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ApplicationVersion));
            await player.SendAsync(new NetworkMessage(MessageType.Login, "Edgeworth"));
            Assert.True(await WaitAsync(() => users.Users.Any(u => u.Name == "Edgeworth")));

            await host.SendOocAsync("five minutes to showtime");

            // the player hears it, prefixed so the author is clear
            Assert.True(await WaitAsync(() => received.Any(m =>
                m.Type == MessageType.OutOfCharacter && m.GetArgument(0) == "[Server] five minutes to showtime")));
            // and the monitor logs the server as the author
            Assert.True(await WaitAsync(() => seen.Any(l => l.Sender == "Server" && l.Text == "five minutes to showtime")));
        }
        finally
        {
            await player.DisconnectAsync();
            await host.StopAsync();
        }
    }
}
