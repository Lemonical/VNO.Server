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
/// Proves the game host stamps its own known identity onto relayed in character lines
/// </summary>
/// <remarks>
/// The client draws the master owned badge from the speaker's shown name, so a spoofed name
/// would draw a badge the sender does not hold. The host must overwrite the first argument
/// with the character the sender actually claimed
/// </remarks>
public sealed class IdentityStampTests
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

    [Fact]
    public async Task In_character_relay_replaces_a_spoofed_name_with_the_claimed_character()
    {
        const int port = 47740;
        var settings = Options.Create(new ServerSettings { ListenPort = port });
        var users = new UserRegistry();
        var server = new TcpMessageServer(NullLogger<TcpMessageServer>.Instance);
        var host = new GameHost(server, users, new BanRegistry(), settings, NullLogger<GameHost>.Instance);
        await host.StartAsync();

        var lines = new ConcurrentQueue<NetworkMessage>();
        await using var speaker = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        await using var observer = new TcpMessageClient(NullLogger<TcpMessageClient>.Instance);
        observer.MessageReceived += (_, e) => { if (e.Message.Type == MessageType.InCharacter) lines.Enqueue(e.Message); };
        try
        {
            await observer.ConnectAsync("127.0.0.1", port);
            await observer.SendAsync(new NetworkMessage(MessageType.Hello, "Judge"));
            await speaker.ConnectAsync("127.0.0.1", port);
            await speaker.SendAsync(new NetworkMessage(MessageType.Hello, "Maya"));
            Assert.True(await WaitAsync(() => users.Users.Count == 2 && users.Users.All(u => u.Name != "Player")));

            // the speaker claims a character, this is the identity the server knows for it
            await speaker.SendAsync(new NetworkMessage(MessageType.PickCharacter, "Phoenix"));
            Assert.True(await WaitAsync(() => users.Users.Any(u => u.Character == "Phoenix")));

            // the speaker tries to pass off a different, badge holding shown name
            await speaker.SendAsync(new NetworkMessage(MessageType.InCharacter, "Godlike", "objection"));

            Assert.True(await WaitAsync(() => lines.Any(m => m.GetArgument(1) == "objection")));
            var relayed = lines.First(m => m.GetArgument(1) == "objection");
            Assert.Equal("Phoenix", relayed.GetArgument(0));
            Assert.NotEqual("Godlike", relayed.GetArgument(0));
        }
        finally
        {
            await speaker.DisconnectAsync();
            await observer.DisconnectAsync();
            await host.StopAsync();
        }
    }
}
