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
/// Exercises the area scoped moderator commands: mass mute, DJ permission, and
/// room lock, proving each takes effect and stays gated on staff status
/// </summary>
public sealed class ModeratorRoomCommandTests
{
    private static async Task<(TcpMessageServer server, GameHost host, UserRegistry users)> StartAsync(int port)
    {
        var settings = Options.Create(new ServerSettings { ListenPort = port, Areas = { "Lobby", "Court" } });
        var users = new UserRegistry();
        var server = new TcpMessageServer(NullLogger<TcpMessageServer>.Instance);
        var host = new GameHost(server, users, new BanRegistry(), new FakeAuthLink(), settings, NullLogger<GameHost>.Instance);
        await host.StartAsync();
        return (server, host, users);
    }

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

    [Fact]
    public async Task Mass_mute_silences_every_non_staff_player_in_the_area()
    {
        const int port = 47681;
        var (server, host, users) = await StartAsync(port);

        await using var mod = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var a = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var b = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        try
        {
            await mod.ConnectAsync("127.0.0.1", port);
            await mod.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await mod.SendAsync(new NetworkMessage(MessageType.Login, "Mod"));
            await a.ConnectAsync("127.0.0.1", port);
            await a.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await a.SendAsync(new NetworkMessage(MessageType.Login, "Alice"));
            await b.ConnectAsync("127.0.0.1", port);
            await b.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await b.SendAsync(new NetworkMessage(MessageType.Login, "Bob"));

            Assert.True(await WaitAsync(
                () => users.Users.Count == 3 && users.Users.All(u => u.Name != "Player")));
            var modUser = users.Users.First(u => u.Name == "Mod");
            modUser.IsModerator = true;

            await mod.SendAsync(NetworkMessage.Create(MessageType.MassMute));

            Assert.True(await WaitAsync(() =>
                users.Users.Where(u => !u.IsModerator).All(u => u.IsMuted)));
            // the moderator themselves stays unmuted
            Assert.False(modUser.IsMuted);
        }
        finally
        {
            await mod.DisconnectAsync();
            await a.DisconnectAsync();
            await b.DisconnectAsync();
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Dj_off_blocks_the_player_from_changing_music()
    {
        const int port = 47682;
        var (server, host, users) = await StartAsync(port);

        var heard = new ConcurrentQueue<NetworkMessage>();
        await using var mod = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var dj = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var listener = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        listener.MessageReceived += (_, e) => heard.Enqueue(e.Message);
        try
        {
            await mod.ConnectAsync("127.0.0.1", port);
            await mod.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await mod.SendAsync(new NetworkMessage(MessageType.Login, "Mod"));
            await dj.ConnectAsync("127.0.0.1", port);
            await dj.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await dj.SendAsync(new NetworkMessage(MessageType.Login, "Deejay"));
            await listener.ConnectAsync("127.0.0.1", port);
            await listener.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await listener.SendAsync(new NetworkMessage(MessageType.Login, "Ears"));

            Assert.True(await WaitAsync(
                () => users.Users.Count == 3 && users.Users.All(u => u.Name != "Player")));
            var modUser = users.Users.First(u => u.Name == "Mod");
            var djUser = users.Users.First(u => u.Name == "Deejay");
            modUser.IsModerator = true;

            await mod.SendAsync(new NetworkMessage(MessageType.DjOff, djUser.Id.ToString()));
            Assert.True(await WaitAsync(() => !djUser.IsDj));

            heard.Clear();
            await dj.SendAsync(new NetworkMessage(MessageType.Music, "forbidden.mp3"));
            await Task.Delay(400);

            Assert.DoesNotContain(heard, m => m.Type == MessageType.Music);
        }
        finally
        {
            await mod.DisconnectAsync();
            await dj.DisconnectAsync();
            await listener.DisconnectAsync();
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Isolated_player_chat_only_echoes_back_to_themselves()
    {
        const int port = 47688;
        var (server, host, users) = await StartAsync(port);

        var otherHeard = new ConcurrentQueue<NetworkMessage>();
        var selfHeard = new ConcurrentQueue<NetworkMessage>();
        await using var mod = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var isolated = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var other = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        isolated.MessageReceived += (_, e) => selfHeard.Enqueue(e.Message);
        other.MessageReceived += (_, e) => otherHeard.Enqueue(e.Message);
        try
        {
            await mod.ConnectAsync("127.0.0.1", port);
            await mod.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await mod.SendAsync(new NetworkMessage(MessageType.Login, "Mod"));
            await isolated.ConnectAsync("127.0.0.1", port);
            await isolated.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await isolated.SendAsync(new NetworkMessage(MessageType.Login, "Loud"));
            await other.ConnectAsync("127.0.0.1", port);
            await other.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await other.SendAsync(new NetworkMessage(MessageType.Login, "Bystander"));

            Assert.True(await WaitAsync(
                () => users.Users.Count == 3 && users.Users.All(u => u.Name != "Player")));
            var modUser = users.Users.First(u => u.Name == "Mod");
            var loudUser = users.Users.First(u => u.Name == "Loud");
            modUser.IsModerator = true;

            await mod.SendAsync(new NetworkMessage(MessageType.Isolate, loudUser.Id.ToString()));
            Assert.True(await WaitAsync(() => loudUser.IsIsolated));

            otherHeard.Clear();
            selfHeard.Clear();
            await isolated.SendAsync(new NetworkMessage(MessageType.OutOfCharacter, "anyone there"));

            // the isolated player hears their own line, the bystander never does
            Assert.True(await WaitAsync(() => selfHeard.Any(m =>
                m.Type == MessageType.OutOfCharacter && m.GetArgument(0) == "anyone there")));
            Assert.DoesNotContain(otherHeard, m =>
                m.Type == MessageType.OutOfCharacter && m.GetArgument(0) == "anyone there");
        }
        finally
        {
            await mod.DisconnectAsync();
            await isolated.DisconnectAsync();
            await other.DisconnectAsync();
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Locked_area_refuses_a_non_staff_join()
    {
        const int port = 47683;
        var (server, host, users) = await StartAsync(port);

        await using var mod = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var player = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        try
        {
            await mod.ConnectAsync("127.0.0.1", port);
            await mod.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await mod.SendAsync(new NetworkMessage(MessageType.Login, "Mod"));
            await player.ConnectAsync("127.0.0.1", port);
            await player.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await player.SendAsync(new NetworkMessage(MessageType.Login, "Player1"));

            Assert.True(await WaitAsync(
                () => users.Users.Count == 2 && users.Users.All(u => u.Name != "Player")));
            var modUser = users.Users.First(u => u.Name == "Mod");
            var playerUser = users.Users.First(u => u.Name == "Player1");
            modUser.IsModerator = true;

            // the moderator moves to area 1 and locks it
            await mod.SendAsync(new NetworkMessage(MessageType.JoinArea, "1"));
            Assert.True(await WaitAsync(() => modUser.AreaId == 1));
            await mod.SendAsync(NetworkMessage.Create(MessageType.LockRoom));
            await Task.Delay(200);

            // the ordinary player cannot enter the locked area
            await player.SendAsync(new NetworkMessage(MessageType.JoinArea, "1"));
            await Task.Delay(400);
            Assert.Equal(0, playerUser.AreaId);

            // after unlocking, the player can enter
            await mod.SendAsync(NetworkMessage.Create(MessageType.UnlockRoom));
            await Task.Delay(200);
            await player.SendAsync(new NetworkMessage(MessageType.JoinArea, "1"));
            Assert.True(await WaitAsync(() => playerUser.AreaId == 1));
        }
        finally
        {
            await mod.DisconnectAsync();
            await player.DisconnectAsync();
            await host.StopAsync();
        }
    }
}
