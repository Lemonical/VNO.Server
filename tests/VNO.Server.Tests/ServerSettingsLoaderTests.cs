using System;
using System.IO;
using VNO.Core.Networking;
using VNO.Server.Services;
using Xunit;

namespace VNO.Server.Tests;

/// <summary>
/// Covers loading the server settings from the legacy server data files
/// </summary>
public sealed class ServerSettingsLoaderTests : IDisposable
{
    private readonly string _baseDirectory =
        Path.Combine(Path.GetTempPath(), "vno-server-loader-" + Path.GetRandomFileName());

    private string DataDirectory => Path.Combine(_baseDirectory, "data");

    public ServerSettingsLoaderTests() => Directory.CreateDirectory(DataDirectory);

    public void Dispose()
    {
        if (Directory.Exists(_baseDirectory))
        {
            Directory.Delete(_baseDirectory, recursive: true);
        }
    }

    private void WriteData(string fileName, string content) =>
        File.WriteAllText(Path.Combine(DataDirectory, fileName), content);

    [Fact]
    public void Missing_files_fall_back_to_the_built_in_defaults()
    {
        var settings = ServerSettingsLoader.Load(_baseDirectory);

        Assert.Equal(6541, settings.ListenPort);
        Assert.Equal(100, settings.PlayerCapacity);
        Assert.False(settings.IsPublic);
        Assert.Equal("vno-master-rjrun.ondigitalocean.app", MasterServerEndpoint.Host);
        Assert.Equal(443, MasterServerEndpoint.Port);
        Assert.Equal(Transport.WebSocket, MasterServerEndpoint.Transport);
        Assert.True(MasterServerEndpoint.UseTls);
        Assert.Equal(new[] { "Courtroom" }, settings.Areas);
    }

    [Fact]
    public void Reads_the_host_identity_and_credentials_but_ignores_legacy_auth_endpoint_keys()
    {
        WriteData(
            "init.ini",
            "[Server]\nname=Test Court\nport=7001\nplayercapacity=40\npublic=1\nheartbeat=15\nmoderatorpassword=secret\n[AS]\nhost=auth.example\nport=7002\ntransport=tcp\ntls=0\nusername=operator\npassword=credential\nremember=1\n");

        var settings = ServerSettingsLoader.Load(_baseDirectory);

        Assert.Equal("Test Court", settings.Name);
        Assert.Equal(7001, settings.ListenPort);
        Assert.Equal(40, settings.PlayerCapacity);
        Assert.True(settings.IsPublic);
        Assert.Equal(15, settings.HeartbeatSeconds);
        Assert.Equal("secret", settings.ModeratorPassword);
        Assert.Equal("operator", settings.AuthUsername);
        Assert.Equal("credential", settings.AuthPassword);
        Assert.True(settings.AuthRemember);
        Assert.Equal("vno-master-rjrun.ondigitalocean.app", MasterServerEndpoint.Host);
        Assert.Equal(443, MasterServerEndpoint.Port);
    }

    [Fact]
    public void Reads_area_names_from_the_areas_ini_sections()
    {
        WriteData("areas.ini", "[Courtroom]\n[Lobby]\n[Basement]\n");

        var settings = ServerSettingsLoader.Load(_baseDirectory);

        Assert.Equal(new[] { "Courtroom", "Lobby", "Basement" }, settings.Areas);
    }

    [Fact]
    public void Reads_the_music_list_one_track_per_line()
    {
        WriteData("musiclist.txt", "Objection.mp3\n\\\\ comment\nCross-Examination.mp3\n\n");

        var settings = ServerSettingsLoader.Load(_baseDirectory);

        Assert.Equal(new[] { "Objection.mp3", "Cross-Examination.mp3" }, settings.Music);
    }
}
