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
/// Exercises the staff lookup request and response, proving a moderator gets the
/// detail back and that a non staff sender is refused
/// </summary>
public sealed class StaffLookupTests
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
    public async Task Moderator_user_lookup_returns_detail_to_the_requester()
    {
        const int port = 47686;
        var (server, host, users) = await StartAsync(port);

        var modMessages = new ConcurrentQueue<NetworkMessage>();
        await using var mod = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var other = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        mod.MessageReceived += (_, e) => modMessages.Enqueue(e.Message);
        try
        {
            await mod.ConnectAsync("127.0.0.1", port);
            await mod.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await mod.SendAsync(new NetworkMessage(MessageType.Login, "Mod"));
            await other.ConnectAsync("127.0.0.1", port);
            await other.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await other.SendAsync(new NetworkMessage(MessageType.Login, "Target"));

            Assert.True(await WaitAsync(
                () => users.Users.Count == 2 && users.Users.All(u => u.Name != "Player")));
            var modUser = users.Users.First(u => u.Name == "Mod");
            var targetUser = users.Users.First(u => u.Name == "Target");
            modUser.IsModerator = true;

            await mod.SendAsync(new NetworkMessage(MessageType.StaffLookup, "user", targetUser.Id.ToString()));

            Assert.True(await WaitAsync(() => modMessages.Any(m => m.Type == MessageType.StaffLookupResult)));
            var result = modMessages.First(m => m.Type == MessageType.StaffLookupResult).GetArgument(0);
            Assert.Contains("Target", result);
        }
        finally
        {
            await mod.DisconnectAsync();
            await other.DisconnectAsync();
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Non_staff_lookup_is_refused()
    {
        const int port = 47687;
        var (server, host, users) = await StartAsync(port);

        var randoMessages = new ConcurrentQueue<NetworkMessage>();
        await using var rando = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var other = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        rando.MessageReceived += (_, e) => randoMessages.Enqueue(e.Message);
        try
        {
            await rando.ConnectAsync("127.0.0.1", port);
            await rando.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await rando.SendAsync(new NetworkMessage(MessageType.Login, "Rando"));
            await other.ConnectAsync("127.0.0.1", port);
            await other.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await other.SendAsync(new NetworkMessage(MessageType.Login, "Target"));

            Assert.True(await WaitAsync(
                () => users.Users.Count == 2 && users.Users.All(u => u.Name != "Player")));
            var targetUser = users.Users.First(u => u.Name == "Target");

            await rando.SendAsync(new NetworkMessage(MessageType.StaffLookup, "ip", targetUser.Id.ToString()));
            await Task.Delay(400);

            Assert.DoesNotContain(randoMessages, m => m.Type == MessageType.StaffLookupResult);
        }
        finally
        {
            await rando.DisconnectAsync();
            await other.DisconnectAsync();
            await host.StopAsync();
        }
    }
}
