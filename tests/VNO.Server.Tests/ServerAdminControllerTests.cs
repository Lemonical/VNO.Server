using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using VNO.Core.Models;
using VNO.Core.Protocol;
using VNO.Server.Admin;
using VNO.Server.Services;
using Xunit;

namespace VNO.Server.Tests;

/// <summary>
/// Tests for the admin controller every console frontend drives
/// </summary>
public sealed class ServerAdminControllerTests : IDisposable
{
    private readonly FakeGameHost _host = new();
    private readonly FakeModeration _moderation = new();
    private readonly FakeAuthLink _auth = new();
    private readonly UserRegistry _users = new();
    private readonly BanRegistry _bans = new();
    private readonly IssueLog _issues = new();
    private readonly FakeSettingsStore _store = new();
    private readonly ServerSettings _settings = new()
    {
        Name = "Test Server",
        Areas = new() { "Courtroom", "Lobby" },
    };

    private ServerAdminController CreateController() =>
        new(_host, _moderation, _auth, _users, _bans, _issues, _store,
            Options.Create(_settings));

    public void Dispose() => _issues.Dispose();

    [Fact]
    public void Peak_players_tracks_the_highest_count_seen()
    {
        var controller = CreateController();

        _host.PlayerCount = 3;
        _host.RaiseUsersChanged();
        _host.PlayerCount = 1;
        _host.RaiseUsersChanged();

        Assert.Equal(3, controller.GetOverview().PeakPlayers);
        Assert.Equal(1, controller.GetOverview().PlayerCount);
    }

    [Fact]
    public void Chat_lines_are_counted_and_kept_in_history()
    {
        var controller = CreateController();

        _host.RaiseOoc(new OocLine("Zephyr", "hello"));
        _host.RaiseOoc(new OocLine("Server", "welcome"));
        _host.RaiseIc(new IcLine("Phoenix Wright", "Zephyr", "Objection!"));

        var overview = controller.GetOverview();
        Assert.Equal(2, overview.OocMessageCount);
        Assert.Equal(1, overview.IcMessageCount);
        Assert.True(controller.GetOocHistory()[1].IsServer);
        Assert.Equal("Phoenix Wright", controller.GetIcHistory()[0].Character);
    }

    [Fact]
    public void Host_log_lines_are_classified_for_the_event_log()
    {
        var controller = CreateController();

        _host.RaiseLog("Player 1 connected from 10.0.0.1");
        _host.RaiseLog("Moderator 2 kicked player 1");
        _host.RaiseLog("Rejected banned address 10.0.0.9");
        _host.RaiseLog("Server online on port 6541");

        var events = controller.GetEvents();
        Assert.Equal(ConsoleEventKind.Join, events[0].Kind);
        Assert.Equal(ConsoleEventKind.Moderation, events[1].Kind);
        Assert.Equal(ConsoleEventKind.Error, events[2].Kind);
        Assert.Equal(ConsoleEventKind.Info, events[3].Kind);
    }

    [Fact]
    public async Task Applying_config_updates_the_live_settings_and_saves()
    {
        var controller = CreateController();
        var changed = false;
        controller.ConfigChanged += (_, _) => changed = true;

        await controller.ApplyConfigAsync(controller.GetConfig() with
        {
            Name = "Renamed",
            ListenPort = 7100,
            IsPublic = true,
        });

        Assert.Equal("Renamed", _settings.Name);
        Assert.Equal(7100, _settings.ListenPort);
        Assert.True(_settings.IsPublic);
        Assert.Equal(1, _store.SaveCount);
        Assert.True(changed);
    }

    [Fact]
    public async Task List_edits_persist_and_ignore_duplicates_and_blanks()
    {
        var controller = CreateController();

        await controller.AddAreaAsync("Detention Center");
        await controller.AddAreaAsync("  detention center ");
        await controller.AddAreaAsync("   ");
        await controller.RemoveMusicAsync("not there");

        Assert.Equal(new[] { "Courtroom", "Lobby", "Detention Center" }, controller.GetAreas());
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public void Removing_a_ban_raises_the_change_event()
    {
        var controller = CreateController();
        var raised = 0;
        controller.BansChanged += (_, _) => raised++;

        _bans.Add(new BanEntry { Kind = BanKind.Account, Target = "Griefer" });
        Assert.True(controller.RemoveBan(BanKind.Account, "Griefer"));
        Assert.False(controller.RemoveBan(BanKind.Account, "Griefer"));

        // one for the add, one for the successful removal
        Assert.Equal(2, raised);
        Assert.Empty(controller.GetBans());
    }

    [Fact]
    public async Task Granting_moderator_notifies_the_player_and_the_console()
    {
        var controller = CreateController();
        var user = _users.Add("session-1", "10.0.0.1");
        var playersChanged = false;
        controller.PlayersChanged += (_, _) => playersChanged = true;

        await controller.SetModeratorAsync(user.Id, granted: true);

        Assert.True(user.IsModerator);
        var sent = Assert.Single(_host.Sent);
        Assert.Equal(user.Id, sent.UserId);
        Assert.Equal(MessageType.ModeratorGranted, sent.Message.Type);
        Assert.True(playersChanged);
        Assert.Contains(controller.GetEvents(), e => e.Kind == ConsoleEventKind.Moderation);
    }

    [Fact]
    public async Task Console_bans_flow_through_moderation_with_the_admin_actor()
    {
        var controller = CreateController();
        var user = _users.Add("session-1", "10.0.0.1");

        await controller.BanAccountAsync(user.Id, "", TimeSpan.FromDays(7));
        await controller.BanAddressAsync("10.0.0.1", "bots", null);

        Assert.Equal(("Banned by staff", "admin", TimeSpan.FromDays(7)), _moderation.LastAccountBan);
        Assert.Equal(("bots", "admin", (TimeSpan?)null), _moderation.LastAddressBan);
    }

    [Fact]
    public void Players_snapshot_resolves_area_names()
    {
        var controller = CreateController();
        var user = _users.Add("session-1", "10.0.0.1");
        user.Name = "Zephyr";
        user.AreaId = 1;

        var snapshot = Assert.Single(controller.GetPlayers());
        Assert.Equal("Lobby", snapshot.AreaName);
        Assert.Equal("Zephyr", snapshot.Name);
    }

}
