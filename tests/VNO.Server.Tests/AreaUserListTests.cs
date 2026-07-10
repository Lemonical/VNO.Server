using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
/// Exercises per area user lists and area switching over a real TCP link
/// </summary>
public sealed class AreaUserListTests
{
    private static TcpMessageClient TrackedClient(ConcurrentQueue<NetworkMessage> sink)
    {
        var client = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        client.MessageReceived += (_, e) => sink.Enqueue(e.Message);
        return client;
    }

    private static async Task<NetworkMessage?> WaitForUserListAsync(
        ConcurrentQueue<NetworkMessage> sink, Func<NetworkMessage, bool> predicate)
    {
        for (var i = 0; i < 50; i++)
        {
            foreach (var m in sink.ToArray())
            {
                if (m.Type == MessageType.UserList && predicate(m))
                {
                    return m;
                }
            }
            await Task.Delay(50);
        }
        return null;
    }

    [Fact]
    public async Task Two_players_in_one_area_see_each_other_and_area_switch_updates_lists()
    {
        const int port = 47655;
        var settings = Options.Create(new ServerSettings
        {
            ListenPort = port,
            Areas = new List<string> { "Courtroom", "Lobby" },
        });

        var server = new TcpMessageServer(NullLogger<TcpMessageServer>.Instance);
        var host = new GameHost(server, new UserRegistry(), new BanRegistry(), new FakeAuthLink(), settings, NullLogger<GameHost>.Instance);
        await host.StartAsync();

        var aMessages = new ConcurrentQueue<NetworkMessage>();
        var bMessages = new ConcurrentQueue<NetworkMessage>();
        await using var a = TrackedClient(aMessages);
        await using var b = TrackedClient(bMessages);

        try
        {
            await a.ConnectAsync("127.0.0.1", port);
            await a.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ApplicationVersion));
            await a.SendAsync(new NetworkMessage(MessageType.Login, "Alice"));
            await b.ConnectAsync("127.0.0.1", port);
            await b.SendAsync(new NetworkMessage(MessageType.VersionCheck, "client", ProtocolConstants.ApplicationVersion));
            await b.SendAsync(new NetworkMessage(MessageType.Login, "Bob"));

            // both start in area 0, so each should see a list naming both. Wait
            // for the settled list, a premature one can name Bob "Player" before
            // his hello sets the name
            var both = await WaitForUserListAsync(
                bMessages, m => m.Arguments.Contains("Alice") && m.Arguments.Contains("Bob"));
            Assert.NotNull(both);
            Assert.Contains("Alice", both!.Arguments);
            Assert.Contains("Bob", both.Arguments);

            // Bob moves to area 1, area 0 should drop back to just Alice
            await b.SendAsync(new NetworkMessage(MessageType.JoinArea, "1"));

            var aliceAlone = await WaitForUserListAsync(
                aMessages, m => m.Arguments.Count == 1 && m.Arguments[0] == "Alice");
            Assert.NotNull(aliceAlone);
        }
        finally
        {
            await a.DisconnectAsync();
            await b.DisconnectAsync();
            await host.StopAsync();
        }
    }
}
