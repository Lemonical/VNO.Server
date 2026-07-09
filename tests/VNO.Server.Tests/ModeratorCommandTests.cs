using System;
using System.Collections.Generic;
using System.Linq;
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
/// Exercises moderator commands arriving over the network, proving they act on
/// the target and that non staff senders are ignored
/// </summary>
public sealed class ModeratorCommandTests
{
    private static async Task<(TcpMessageServer server, GameHost host, UserRegistry users)> StartAsync(int port)
    {
        var settings = Options.Create(new ServerSettings { ListenPort = port });
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
    public async Task Moderator_can_mute_a_target_player()
    {
        const int port = 47661;
        var (server, host, users) = await StartAsync(port);

        await using var mod = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var victim = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        try
        {
            await mod.ConnectAsync("127.0.0.1", port);
            await mod.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await mod.SendAsync(new NetworkMessage(MessageType.Login, "Mod"));
            await victim.ConnectAsync("127.0.0.1", port);
            await victim.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await victim.SendAsync(new NetworkMessage(MessageType.Login, "Victim"));

            Assert.True(
                await WaitAsync(() => users.Users.Count == 2 && users.Users.All(u => u.Name != "Player")),
                "both players did not register with their names");

            var modUser = users.Users.First(u => u.Name == "Mod");
            var victimUser = users.Users.First(u => u.Name == "Victim");
            modUser.IsModerator = true;

            await mod.SendAsync(new NetworkMessage(MessageType.Mute, victimUser.Id.ToString()));

            Assert.True(await WaitAsync(() => victimUser.IsMuted), "victim was not muted by the moderator command");
        }
        finally
        {
            await mod.DisconnectAsync();
            await victim.DisconnectAsync();
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Non_moderator_mute_command_is_ignored()
    {
        const int port = 47662;
        var (server, host, users) = await StartAsync(port);

        await using var attacker = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var victim = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        try
        {
            await attacker.ConnectAsync("127.0.0.1", port);
            await attacker.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await attacker.SendAsync(new NetworkMessage(MessageType.Login, "Rando"));
            await victim.ConnectAsync("127.0.0.1", port);
            await victim.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ClientVersion));
            await victim.SendAsync(new NetworkMessage(MessageType.Login, "Victim"));

            Assert.True(
                await WaitAsync(() => users.Users.Count == 2 && users.Users.All(u => u.Name != "Player")),
                "both players did not register with their names");
            var victimUser = users.Users.First(u => u.Name == "Victim");

            // attacker is not a moderator, the command must be ignored
            await attacker.SendAsync(new NetworkMessage(MessageType.Mute, victimUser.Id.ToString()));
            await Task.Delay(400);

            Assert.False(victimUser.IsMuted, "a non moderator was able to mute a player");
        }
        finally
        {
            await attacker.DisconnectAsync();
            await victim.DisconnectAsync();
            await host.StopAsync();
        }
    }
}
