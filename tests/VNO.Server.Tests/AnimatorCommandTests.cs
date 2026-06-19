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
/// Exercises animator stat changes arriving over the network, proving a staff
/// member reaches the target and a non staff sender is ignored
/// </summary>
public sealed class AnimatorCommandTests
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
    public async Task Staff_stat_change_reaches_the_target()
    {
        const int port = 47665;
        var (server, host, users) = await StartAsync(port);

        var victimMessages = new ConcurrentQueue<NetworkMessage>();
        await using var animator = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var victim = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        victim.MessageReceived += (_, e) => victimMessages.Enqueue(e.Message);
        try
        {
            await animator.ConnectAsync("127.0.0.1", port);
            await animator.SendAsync(new NetworkMessage(MessageType.Hello, "Anim"));
            await victim.ConnectAsync("127.0.0.1", port);
            await victim.SendAsync(new NetworkMessage(MessageType.Hello, "Victim"));

            Assert.True(await WaitAsync(
                () => users.Users.Count == 2 && users.Users.All(u => u.Name != "Player")));
            users.Users.First(u => u.Name == "Anim").IsModerator = true;
            var victimId = users.Users.First(u => u.Name == "Victim").Id;

            await animator.SendAsync(new NetworkMessage(
                MessageType.StatChange, victimId.ToString(), "HP", "50"));

            var stat = await WaitAsync(() => victimMessages.Any(m =>
                m.Type == MessageType.StatChange && m.GetArgument(1) == "HP" && m.GetArgument(2) == "50"));
            Assert.True(stat, "the target never received the stat change");
        }
        finally
        {
            await animator.DisconnectAsync();
            await victim.DisconnectAsync();
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Staff_give_item_reaches_the_target()
    {
        const int port = 47671;
        var (server, host, users) = await StartAsync(port);

        var victimMessages = new ConcurrentQueue<NetworkMessage>();
        await using var animator = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var victim = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        victim.MessageReceived += (_, e) => victimMessages.Enqueue(e.Message);
        try
        {
            await animator.ConnectAsync("127.0.0.1", port);
            await animator.SendAsync(new NetworkMessage(MessageType.Hello, "Anim"));
            await victim.ConnectAsync("127.0.0.1", port);
            await victim.SendAsync(new NetworkMessage(MessageType.Hello, "Victim"));

            Assert.True(await WaitAsync(
                () => users.Users.Count == 2 && users.Users.All(u => u.Name != "Player")));
            users.Users.First(u => u.Name == "Anim").IsModerator = true;
            var victimId = users.Users.First(u => u.Name == "Victim").Id;

            await animator.SendAsync(new NetworkMessage(
                MessageType.GiveItem, victimId.ToString(), "Potion"));

            var got = await WaitAsync(() => victimMessages.Any(m =>
                m.Type == MessageType.GiveItem && m.GetArgument(1) == "Potion"));
            Assert.True(got, "the target never received the item");
        }
        finally
        {
            await animator.DisconnectAsync();
            await victim.DisconnectAsync();
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Non_staff_stat_change_is_not_forwarded()
    {
        const int port = 47666;
        var (server, host, users) = await StartAsync(port);

        var victimMessages = new ConcurrentQueue<NetworkMessage>();
        await using var rando = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var victim = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        victim.MessageReceived += (_, e) => victimMessages.Enqueue(e.Message);
        try
        {
            await rando.ConnectAsync("127.0.0.1", port);
            await rando.SendAsync(new NetworkMessage(MessageType.Hello, "Rando"));
            await victim.ConnectAsync("127.0.0.1", port);
            await victim.SendAsync(new NetworkMessage(MessageType.Hello, "Victim"));

            Assert.True(await WaitAsync(
                () => users.Users.Count == 2 && users.Users.All(u => u.Name != "Player")));
            var victimId = users.Users.First(u => u.Name == "Victim").Id;

            await rando.SendAsync(new NetworkMessage(
                MessageType.StatChange, victimId.ToString(), "HP", "50"));
            await Task.Delay(400);

            Assert.DoesNotContain(victimMessages, m => m.Type == MessageType.StatChange);
        }
        finally
        {
            await rando.DisconnectAsync();
            await victim.DisconnectAsync();
            await host.StopAsync();
        }
    }
}
