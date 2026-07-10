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
/// Exercises the staff started countdown timer broadcast over a real TCP link
/// </summary>
public sealed class TimerBroadcastTests
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
    public async Task Staff_timer_is_broadcast_to_all_players()
    {
        const int port = 47667;
        var (server, host, users) = await StartAsync(port);

        var otherMessages = new ConcurrentQueue<NetworkMessage>();
        await using var staff = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var other = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        other.MessageReceived += (_, e) => otherMessages.Enqueue(e.Message);
        try
        {
            await staff.ConnectAsync("127.0.0.1", port);
            await staff.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ApplicationVersion));
            await staff.SendAsync(new NetworkMessage(MessageType.Login, "Staff"));
            await other.ConnectAsync("127.0.0.1", port);
            await other.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ApplicationVersion));
            await other.SendAsync(new NetworkMessage(MessageType.Login, "Other"));

            Assert.True(await WaitAsync(
                () => users.Users.Count == 2 && users.Users.All(u => u.Name != "Player")));
            users.Users.First(u => u.Name == "Staff").IsModerator = true;

            await staff.SendAsync(new NetworkMessage(MessageType.Timer, "90"));

            var got = await WaitAsync(() => otherMessages.Any(m =>
                m.Type == MessageType.Timer && m.GetArgument(0) == "90"));
            Assert.True(got, "the other player never received the timer");
        }
        finally
        {
            await staff.DisconnectAsync();
            await other.DisconnectAsync();
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Non_staff_timer_is_ignored()
    {
        const int port = 47668;
        var (server, host, users) = await StartAsync(port);

        var otherMessages = new ConcurrentQueue<NetworkMessage>();
        await using var rando = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var other = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        other.MessageReceived += (_, e) => otherMessages.Enqueue(e.Message);
        try
        {
            await rando.ConnectAsync("127.0.0.1", port);
            await rando.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ApplicationVersion));
            await rando.SendAsync(new NetworkMessage(MessageType.Login, "Rando"));
            await other.ConnectAsync("127.0.0.1", port);
            await other.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ApplicationVersion));
            await other.SendAsync(new NetworkMessage(MessageType.Login, "Other"));

            Assert.True(await WaitAsync(
                () => users.Users.Count == 2 && users.Users.All(u => u.Name != "Player")));

            await rando.SendAsync(new NetworkMessage(MessageType.Timer, "90"));
            await Task.Delay(400);

            Assert.DoesNotContain(otherMessages, m => m.Type == MessageType.Timer);
        }
        finally
        {
            await rando.DisconnectAsync();
            await other.DisconnectAsync();
            await host.StopAsync();
        }
    }
}
