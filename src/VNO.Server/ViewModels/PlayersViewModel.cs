using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNO.Server.Admin;
using VNO.Server.Services;

namespace VNO.Server.ViewModels;

/// <summary>
/// View model for the players table and the detail pane with moderation actions
/// </summary>
public sealed partial class PlayersViewModel : ViewModelBase
{
    private readonly IServerAdminController _admin;
    private readonly IConsoleInteraction _interaction;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private PlayerItemViewModel? _selectedPlayer;

    [ObservableProperty]
    private bool _isDetailOpen;

    [ObservableProperty]
    private string _countText = "0 connected";

    [ObservableProperty]
    private bool _hasPlayers;

    [ObservableProperty]
    private string _muteToggleText = "Mute";

    [ObservableProperty]
    private string _moderatorToggleText = "Make Moderator";

    /// <summary>
    /// Creates the players page over the admin controller
    /// </summary>
    public PlayersViewModel(IServerAdminController admin, IConsoleInteraction interaction)
    {
        _admin = admin;
        _interaction = interaction;
        _admin.PlayersChanged += (_, _) => Dispatcher.UIThread.Post(Rebuild);
        _admin.ConfigChanged += (_, _) => Dispatcher.UIThread.Post(Rebuild);
        Rebuild();
    }

    /// <summary>
    /// Players matching the search, ordered by id
    /// </summary>
    public ObservableCollection<PlayerItemViewModel> Players { get; } = new();

    partial void OnQueryChanged(string value) => Rebuild();

    partial void OnSelectedPlayerChanged(PlayerItemViewModel? value)
    {
        foreach (var player in Players)
        {
            player.IsSelected = ReferenceEquals(player, value);
        }
        IsDetailOpen = value is not null;
        MuteToggleText = value?.IsMuted == true ? "Unmute" : "Mute";
        ModeratorToggleText = value?.IsModerator == true ? "Revoke Moderator" : "Make Moderator";
    }

    [RelayCommand]
    private void Select(PlayerItemViewModel player) => SelectedPlayer = player;

    [RelayCommand]
    private void CloseDetail() => SelectedPlayer = null;

    [RelayCommand]
    private async Task ToggleMuteAsync()
    {
        if (SelectedPlayer is not { } player)
        {
            return;
        }
        var mute = !player.IsMuted;
        await _admin.SetMutedAsync(player.Id, mute).ConfigureAwait(true);
        _interaction.ShowToast($"{player.Name} {(mute ? "muted" : "unmuted")}", ToastSeverity.Success);
    }

    [RelayCommand]
    private async Task ToggleModeratorAsync()
    {
        if (SelectedPlayer is not { } player)
        {
            return;
        }
        var grant = !player.IsModerator;
        await _admin.SetModeratorAsync(player.Id, grant).ConfigureAwait(true);
        _interaction.ShowToast(
            $"{player.Name} {(grant ? "is now a moderator" : "lost moderator powers")}",
            ToastSeverity.Success);
    }

    [RelayCommand]
    private async Task KickAsync()
    {
        if (SelectedPlayer is not { } player)
        {
            return;
        }
        var result = await _interaction.ShowModalAsync(new ModalRequest(
            "Kick Player",
            $"Kick {player.Name} from the server? They can reconnect.",
            "Kick",
            IsDestructive: true,
            ShowReason: true)).ConfigureAwait(true);
        if (result is null)
        {
            return;
        }
        await _admin.KickAsync(player.Id, result.Reason).ConfigureAwait(true);
        _interaction.ShowToast($"{player.Name} kicked", ToastSeverity.Success);
    }

    [RelayCommand]
    private async Task BanAsync()
    {
        if (SelectedPlayer is not { } player)
        {
            return;
        }
        var result = await _interaction.ShowModalAsync(new ModalRequest(
            "Ban Account",
            $"Ban the account {player.Name}? They will be disconnected.",
            "Ban",
            IsDestructive: true,
            ShowReason: true,
            ShowDuration: true)).ConfigureAwait(true);
        if (result is null)
        {
            return;
        }
        await _admin.BanAccountAsync(player.Id, result.Reason, result.Duration).ConfigureAwait(true);
        _interaction.ShowToast($"{player.Name} banned", ToastSeverity.Success);
    }

    [RelayCommand]
    private async Task BanIpAsync()
    {
        if (SelectedPlayer is not { } player)
        {
            return;
        }
        var result = await _interaction.ShowModalAsync(new ModalRequest(
            "Ban IP Address",
            $"Ban IP {player.IpAddress}? All players on this address will be disconnected.",
            "Ban IP",
            IsDestructive: true,
            ShowReason: true,
            ShowDuration: true)).ConfigureAwait(true);
        if (result is null)
        {
            return;
        }
        await _admin.BanAddressAsync(player.IpAddress, result.Reason, result.Duration).ConfigureAwait(true);
        _interaction.ShowToast($"IP {player.IpAddress} banned", ToastSeverity.Success);
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (SelectedPlayer is not { } player)
        {
            return;
        }
        await _admin.DisconnectAsync(player.Id).ConfigureAwait(true);
        _interaction.ShowToast($"{player.Name} disconnected");
    }

    private void Rebuild()
    {
        var selectedId = SelectedPlayer?.Id;
        var query = Query.Trim();
        var snapshots = _admin.GetPlayers()
            .Where(p => query.Length == 0 ||
                p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Character.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.IpAddress.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Players.Clear();
        PlayerItemViewModel? reselect = null;
        foreach (var snapshot in snapshots)
        {
            var item = new PlayerItemViewModel(snapshot);
            if (snapshot.Id == selectedId)
            {
                reselect = item;
            }
            Players.Add(item);
        }

        var total = _admin.GetPlayers().Count;
        CountText = $"{total} connected";
        HasPlayers = Players.Count > 0;
        SelectedPlayer = reselect;
    }
}
