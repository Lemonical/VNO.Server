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
/// Exercises the in game moderator authentication flow over a real TCP link
/// </summary>
public sealed class ModeratorAuthTests
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

    private static async Task<(TcpMessageServer, GameHost, UserRegistry)> StartAsync(int port, string password)
    {
        var settings = Options.Create(new ServerSettings { ListenPort = port, ModeratorPassword = password });
        var users = new UserRegistry();
        var server = new TcpMessageServer(NullLogger<TcpMessageServer>.Instance);
        var host = new GameHost(server, users, new BanRegistry(), settings, NullLogger<GameHost>.Instance);
        await host.StartAsync();
        return (server, host, users);
    }

    [Fact]
    public async Task Correct_password_grants_moderator_status()
    {
        const int port = 47663;
        var (server, host, users) = await StartAsync(port, "letmein");

        var replies = new ConcurrentQueue<NetworkMessage>();
        await using var client = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        client.MessageReceived += (_, e) => replies.Enqueue(e.Message);
        try
        {
            await client.ConnectAsync("127.0.0.1", port);
            await client.SendAsync(new NetworkMessage(MessageType.Hello, "Staffer"));
            Assert.True(await WaitAsync(() => users.Users.Any(u => u.Name == "Staffer")));

            await client.SendAsync(new NetworkMessage(MessageType.ModeratorAuth, "letmein"));

            Assert.True(await WaitAsync(() => replies.Any(m => m.Type == MessageType.ModeratorGranted)),
                "no grant reply received");
            Assert.True(users.Users.First(u => u.Name == "Staffer").IsModerator);
        }
        finally
        {
            await client.DisconnectAsync();
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Wrong_password_is_denied_and_grants_nothing()
    {
        const int port = 47664;
        var (server, host, users) = await StartAsync(port, "letmein");

        var replies = new ConcurrentQueue<NetworkMessage>();
        await using var client = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        client.MessageReceived += (_, e) => replies.Enqueue(e.Message);
        try
        {
            await client.ConnectAsync("127.0.0.1", port);
            await client.SendAsync(new NetworkMessage(MessageType.Hello, "Sneaky"));
            Assert.True(await WaitAsync(() => users.Users.Any(u => u.Name == "Sneaky")));

            await client.SendAsync(new NetworkMessage(MessageType.ModeratorAuth, "guess"));

            Assert.True(await WaitAsync(() => replies.Any(m => m.Type == MessageType.ModeratorDenied)),
                "no deny reply received");
            Assert.False(users.Users.First(u => u.Name == "Sneaky").IsModerator);
        }
        finally
        {
            await client.DisconnectAsync();
            await host.StopAsync();
        }
    }
}
