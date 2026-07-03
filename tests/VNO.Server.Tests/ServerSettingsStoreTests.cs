using System;
using System.IO;
using System.Threading.Tasks;
using VNO.Core.Networking;
using VNO.Server.Services;
using Xunit;

namespace VNO.Server.Tests;

/// <summary>
/// Tests that saved settings read back through the loader unchanged
/// </summary>
public sealed class ServerSettingsStoreTests : IDisposable
{
    private readonly string _baseDirectory =
        Path.Combine(Path.GetTempPath(), "vno-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_baseDirectory))
        {
            Directory.Delete(_baseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Settings_round_trip_through_the_data_files()
    {
        var settings = new ServerSettings
        {
            Name = "Turnabout Central",
            ListenPort = 7001,
            ListenTransport = Transport.WebSocket,
            IsPublic = true,
            HeartbeatSeconds = 25,
            ModeratorPassword = "hunter2",
            ChatBurst = 5,
            ChatMessagesPerSecond = 1.5,
            AuthServerHost = "auth.example.test",
            AuthServerPort = 9001,
            AuthTransport = Transport.WebSocket,
            AuthUseTls = true,
            AuthUsername = "operator",
            AuthPassword = "swordfish",
            AuthRemember = true,
            Areas = new() { "Courtroom", "Lobby", "Detention Center" },
            Music = new() { "Trial", "Pursuit ~ Cornered" },
            Characters = new() { "Phoenix Wright" },
        };

        await new ServerSettingsStore(_baseDirectory).SaveAsync(settings);
        var loaded = ServerSettingsLoader.Load(_baseDirectory);

        Assert.Equal(settings.Name, loaded.Name);
        Assert.Equal(settings.ListenPort, loaded.ListenPort);
        Assert.Equal(settings.ListenTransport, loaded.ListenTransport);
        Assert.Equal(settings.IsPublic, loaded.IsPublic);
        Assert.Equal(settings.HeartbeatSeconds, loaded.HeartbeatSeconds);
        Assert.Equal(settings.ModeratorPassword, loaded.ModeratorPassword);
        Assert.Equal(settings.ChatBurst, loaded.ChatBurst);
        Assert.Equal(settings.ChatMessagesPerSecond, loaded.ChatMessagesPerSecond);
        Assert.Equal(settings.AuthServerHost, loaded.AuthServerHost);
        Assert.Equal(settings.AuthServerPort, loaded.AuthServerPort);
        Assert.Equal(settings.AuthTransport, loaded.AuthTransport);
        Assert.Equal(settings.AuthUseTls, loaded.AuthUseTls);
        Assert.Equal(settings.AuthUsername, loaded.AuthUsername);
        Assert.Equal(settings.AuthPassword, loaded.AuthPassword);
        Assert.Equal(settings.AuthRemember, loaded.AuthRemember);
        Assert.Equal(settings.Areas, loaded.Areas);
        Assert.Equal(settings.Music, loaded.Music);
        Assert.Equal(settings.Characters, loaded.Characters);
    }

    [Fact]
    public async Task Disabled_flags_survive_the_round_trip()
    {
        var settings = new ServerSettings
        {
            IsPublic = false,
            AuthUseTls = false,
        };

        await new ServerSettingsStore(_baseDirectory).SaveAsync(settings);
        var loaded = ServerSettingsLoader.Load(_baseDirectory);

        Assert.False(loaded.IsPublic);
        Assert.False(loaded.AuthUseTls);
        Assert.Equal(Transport.Tcp, loaded.ListenTransport);
    }
}
