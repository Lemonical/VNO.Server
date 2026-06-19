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
/// Exercises staff image and forced music broadcasts over a real TCP link
/// </summary>
public sealed class StaffBroadcastTests
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
        var host = new GameHost(server, users, new BanRegistry(), settings, NullLogger<GameHost>.Instance);
        await host.StartAsync();
        return (server, host, users);
    }

    [Fact]
    public async Task Staff_image_broadcast_reaches_all_players()
    {
        const int port = 47669;
        var (server, host, users) = await StartAsync(port);

        var otherMessages = new ConcurrentQueue<NetworkMessage>();
        await using var staff = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var other = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        other.MessageReceived += (_, e) => otherMessages.Enqueue(e.Message);
        try
        {
            await staff.ConnectAsync("127.0.0.1", port);
            await staff.SendAsync(new NetworkMessage(MessageType.Hello, "Staff"));
            await other.ConnectAsync("127.0.0.1", port);
            await other.SendAsync(new NetworkMessage(MessageType.Hello, "Other"));

            Assert.True(await WaitAsync(
                () => users.Users.Count == 2 && users.Users.All(u => u.Name != "Player")));
            users.Users.First(u => u.Name == "Staff").IsModerator = true;

            await staff.SendAsync(new NetworkMessage(MessageType.StreamImage, "http://example/pic.png"));

            var got = await WaitAsync(() => otherMessages.Any(m =>
                m.Type == MessageType.StreamImage && m.GetArgument(0) == "http://example/pic.png"));
            Assert.True(got, "the other player never received the image broadcast");
        }
        finally
        {
            await staff.DisconnectAsync();
            await other.DisconnectAsync();
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Non_staff_image_broadcast_is_ignored()
    {
        const int port = 47670;
        var (server, host, users) = await StartAsync(port);

        var otherMessages = new ConcurrentQueue<NetworkMessage>();
        await using var rando = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var other = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        other.MessageReceived += (_, e) => otherMessages.Enqueue(e.Message);
        try
        {
            await rando.ConnectAsync("127.0.0.1", port);
            await rando.SendAsync(new NetworkMessage(MessageType.Hello, "Rando"));
            await other.ConnectAsync("127.0.0.1", port);
            await other.SendAsync(new NetworkMessage(MessageType.Hello, "Other"));

            Assert.True(await WaitAsync(
                () => users.Users.Count == 2 && users.Users.All(u => u.Name != "Player")));

            await rando.SendAsync(new NetworkMessage(MessageType.StreamImage, "http://evil/pic.png"));
            await Task.Delay(400);

            Assert.DoesNotContain(otherMessages, m => m.Type == MessageType.StreamImage);
        }
        finally
        {
            await rando.DisconnectAsync();
            await other.DisconnectAsync();
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Staff_server_wide_message_reaches_all_players()
    {
        const int port = 47684;
        var (server, host, users) = await StartAsync(port);

        var otherMessages = new ConcurrentQueue<NetworkMessage>();
        await using var staff = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var other = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        other.MessageReceived += (_, e) => otherMessages.Enqueue(e.Message);
        try
        {
            await staff.ConnectAsync("127.0.0.1", port);
            await staff.SendAsync(new NetworkMessage(MessageType.Hello, "Staff"));
            await other.ConnectAsync("127.0.0.1", port);
            await other.SendAsync(new NetworkMessage(MessageType.Hello, "Other"));

            Assert.True(await WaitAsync(
                () => users.Users.Count == 2 && users.Users.All(u => u.Name != "Player")));
            users.Users.First(u => u.Name == "Staff").IsModerator = true;

            await staff.SendAsync(new NetworkMessage(MessageType.Notice, "Round starts now"));

            var got = await WaitAsync(() => otherMessages.Any(m =>
                m.Type == MessageType.Notice && m.GetArgument(0) == "Round starts now"));
            Assert.True(got, "the other player never received the server wide message");
        }
        finally
        {
            await staff.DisconnectAsync();
            await other.DisconnectAsync();
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Non_staff_server_wide_message_is_ignored()
    {
        const int port = 47685;
        var (server, host, users) = await StartAsync(port);

        var otherMessages = new ConcurrentQueue<NetworkMessage>();
        await using var rando = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var other = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        other.MessageReceived += (_, e) => otherMessages.Enqueue(e.Message);
        try
        {
            await rando.ConnectAsync("127.0.0.1", port);
            await rando.SendAsync(new NetworkMessage(MessageType.Hello, "Rando"));
            await other.ConnectAsync("127.0.0.1", port);
            await other.SendAsync(new NetworkMessage(MessageType.Hello, "Other"));

            Assert.True(await WaitAsync(
                () => users.Users.Count == 2 && users.Users.All(u => u.Name != "Player")));

            // a non staff notice must not be relayed, only staff may broadcast
            await rando.SendAsync(new NetworkMessage(MessageType.Notice, "spoofed notice"));
            await Task.Delay(400);

            Assert.DoesNotContain(otherMessages, m =>
                m.Type == MessageType.Notice && m.GetArgument(0) == "spoofed notice");
        }
        finally
        {
            await rando.DisconnectAsync();
            await other.DisconnectAsync();
            await host.StopAsync();
        }
    }
}
