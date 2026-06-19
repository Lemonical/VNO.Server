using System;
using System.Collections.Concurrent;
using System.Threading;
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
/// Exercises the join handshake over a real TCP loopback link, proving the game
/// host streams its areas and music to a player right after the hello
/// </summary>
public sealed class JoinStreamingTests
{
    [Fact]
    public async Task Joining_player_receives_area_and_music_lists()
    {
        const int port = 47653;
        var settings = Options.Create(new ServerSettings
        {
            ListenPort = port,
            Areas = new System.Collections.Generic.List<string> { "Courtroom", "Lobby" },
            Music = new System.Collections.Generic.List<string> { "Cornered.mp3", "Objection.mp3" },
        });

        var server = new TcpMessageServer(NullLogger<TcpMessageServer>.Instance);
        var host = new GameHost(server, new UserRegistry(), new BanRegistry(), settings, NullLogger<GameHost>.Instance);
        await host.StartAsync();

        var received = new ConcurrentBag<NetworkMessage>();
        var gotArea = new TaskCompletionSource();
        var gotMusic = new TaskCompletionSource();

        await using var client = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        client.MessageReceived += (_, e) =>
        {
            received.Add(e.Message);
            if (e.Message.Type == MessageType.AreaList)
            {
                gotArea.TrySetResult();
            }
            if (e.Message.Type == MessageType.MusicList)
            {
                gotMusic.TrySetResult();
            }
        };

        try
        {
            await client.ConnectAsync("127.0.0.1", port);
            await client.SendAsync(new NetworkMessage(MessageType.Hello, "Tester"));

            var completed = await Task.WhenAny(
                Task.WhenAll(gotArea.Task, gotMusic.Task),
                Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.True(gotArea.Task.IsCompleted, "did not receive the area list");
            Assert.True(gotMusic.Task.IsCompleted, "did not receive the music list");

            NetworkMessage? area = null;
            NetworkMessage? music = null;
            foreach (var m in received)
            {
                if (m.Type == MessageType.AreaList)
                {
                    area = m;
                }
                if (m.Type == MessageType.MusicList)
                {
                    music = m;
                }
            }

            Assert.Equal(new[] { "Courtroom", "Lobby" }, area!.Arguments);
            Assert.Equal(new[] { "Cornered.mp3", "Objection.mp3" }, music!.Arguments);
        }
        finally
        {
            await client.DisconnectAsync();
            await host.StopAsync();
        }
    }
}
