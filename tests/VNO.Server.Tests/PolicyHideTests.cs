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
/// Exercises the room policies, the hide feature, self HP editing, and inventory checks
/// </summary>
public sealed class PolicyHideTests
{
    private static async Task<bool> WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 80; i++)
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
    public async Task A_player_can_hide_only_when_staff_allow_it()
    {
        const int port = 47730;
        var (_, host, users) = await StartAsync(port);

        var lists = new ConcurrentQueue<NetworkMessage>();
        var policies = new ConcurrentQueue<NetworkMessage>();
        await using var staff = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var player = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        staff.MessageReceived += (_, e) => { if (e.Message.Type == MessageType.UserList) lists.Enqueue(e.Message); };
        player.MessageReceived += (_, e) => { if (e.Message.Type == MessageType.RoomPolicy) policies.Enqueue(e.Message); };
        try
        {
            await staff.ConnectAsync("127.0.0.1", port);
            await staff.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await staff.SendAsync(new NetworkMessage(MessageType.Login, "Judge"));
            await player.ConnectAsync("127.0.0.1", port);
            await player.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await player.SendAsync(new NetworkMessage(MessageType.Login, "Maya"));
            Assert.True(await WaitAsync(
                () => users.Users.Count == 2 && users.Users.All(u => u.Name != "Player")));
            users.Users.First(u => u.Name == "Judge").IsModerator = true;

            // hiding is refused while the policy is off
            await player.SendAsync(new NetworkMessage(MessageType.HideSelf, "on"));
            await Task.Delay(300);
            Assert.False(users.Users.First(u => u.Name == "Maya").IsHidden);

            // staff turns hiding on, wait until that policy actually reached the player
            // before hiding so the two connections cannot race
            await staff.SendAsync(new NetworkMessage(MessageType.RoomPolicy, "allowhide", "on"));
            Assert.True(await WaitAsync(() => policies.Any(p => p.GetArgument(0) == "allowhide" && p.GetArgument(1) == "on")));

            await player.SendAsync(new NetworkMessage(MessageType.HideSelf, "on"));

            Assert.True(await WaitAsync(() => users.Users.First(u => u.Name == "Maya").IsHidden));
            Assert.True(await WaitAsync(() =>
            {
                var snapshot = lists.ToArray();
                return snapshot.Length > 0 &&
                    snapshot[^1].Arguments.All(a => a != "Maya") &&
                    snapshot[^1].Arguments.Contains("Judge");
            }));
        }
        finally
        {
            await staff.DisconnectAsync();
            await player.DisconnectAsync();
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Self_hp_editing_is_gated_by_the_policy()
    {
        const int port = 47731;
        var (_, host, users) = await StartAsync(port);

        var echoes = new ConcurrentQueue<NetworkMessage>();
        await using var staff = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var player = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        player.MessageReceived += (_, e) => { if (e.Message.Type == MessageType.StatChange) echoes.Enqueue(e.Message); };
        try
        {
            await staff.ConnectAsync("127.0.0.1", port);
            await staff.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await staff.SendAsync(new NetworkMessage(MessageType.Login, "Judge"));
            await player.ConnectAsync("127.0.0.1", port);
            await player.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await player.SendAsync(new NetworkMessage(MessageType.Login, "Maya"));
            Assert.True(await WaitAsync(
                () => users.Users.Count == 2 && users.Users.All(u => u.Name != "Player")));
            users.Users.First(u => u.Name == "Judge").IsModerator = true;

            // without the policy the non staff self edit is dropped
            await player.SendAsync(new NetworkMessage(MessageType.StatChange, "0", "HP", "50"));
            await Task.Delay(300);
            Assert.Empty(echoes);

            // staff enables self editing, now the same edit comes back to the player
            await staff.SendAsync(new NetworkMessage(MessageType.RoomPolicy, "selfhpedit", "on"));
            await Task.Delay(150);
            await player.SendAsync(new NetworkMessage(MessageType.StatChange, "0", "HP", "50"));

            Assert.True(await WaitAsync(() => echoes.Any(m => m.GetArgument(1) == "HP" && m.GetArgument(2) == "50")));
        }
        finally
        {
            await staff.DisconnectAsync();
            await player.DisconnectAsync();
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task An_inventory_check_reports_credits_the_staff_granted()
    {
        const int port = 47732;
        var (_, host, users) = await StartAsync(port);

        var results = new ConcurrentQueue<NetworkMessage>();
        await using var staff = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var player = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        staff.MessageReceived += (_, e) => { if (e.Message.Type == MessageType.StaffLookupResult) results.Enqueue(e.Message); };
        try
        {
            await staff.ConnectAsync("127.0.0.1", port);
            await staff.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await staff.SendAsync(new NetworkMessage(MessageType.Login, "Judge"));
            await player.ConnectAsync("127.0.0.1", port);
            await player.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await player.SendAsync(new NetworkMessage(MessageType.Login, "Maya"));
            Assert.True(await WaitAsync(
                () => users.Users.Count == 2 && users.Users.All(u => u.Name != "Player")));
            users.Users.First(u => u.Name == "Judge").IsModerator = true;
            var maya = users.Users.First(u => u.Name == "Maya");

            await staff.SendAsync(new NetworkMessage(MessageType.GiveItem, maya.Id.ToString(), "credits", "100"));
            await Task.Delay(150);
            await staff.SendAsync(new NetworkMessage(MessageType.CheckInventory, maya.Id.ToString(), "credits"));

            Assert.True(await WaitAsync(() => results.Any(m => m.GetArgument(0) == "Maya: 100 credits")));
        }
        finally
        {
            await staff.DisconnectAsync();
            await player.DisconnectAsync();
            await host.StopAsync();
        }
    }
}
