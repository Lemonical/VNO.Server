using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNO.Server.Admin;
using VNO.Server.Services;

namespace VNO.Server.ViewModels;

/// <summary>
/// View model for the configuration page, general settings, content lists, and staff
/// </summary>
public sealed partial class ConfigurationViewModel : ViewModelBase
{
    private readonly IServerAdminController _admin;
    private readonly IConsoleInteraction _interaction;

    [ObservableProperty]
    private bool _isGeneralTab = true;

    [ObservableProperty]
    private bool _isAreasTab;

    [ObservableProperty]
    private bool _isMusicTab;

    [ObservableProperty]
    private bool _isCharactersTab;

    [ObservableProperty]
    private bool _isStaffTab;

    [ObservableProperty]
    private string _serverName = string.Empty;

    [ObservableProperty]
    private string _portText = string.Empty;

    [ObservableProperty]
    private bool _isPublic;

    [ObservableProperty]
    private string _heartbeatText = string.Empty;

    [ObservableProperty]
    private string _moderatorPassword = string.Empty;

    [ObservableProperty]
    private string _authHost = string.Empty;

    [ObservableProperty]
    private string _authPortText = string.Empty;

    [ObservableProperty]
    private bool _authUseTls;

    [ObservableProperty]
    private string _newArea = string.Empty;

    [ObservableProperty]
    private string _newMusic = string.Empty;

    [ObservableProperty]
    private string _newCharacter = string.Empty;

    [ObservableProperty]
    private bool _hasCharacters;

    [ObservableProperty]
    private bool _hasStaff;

    /// <summary>
    /// Creates the configuration page over the admin controller
    /// </summary>
    public ConfigurationViewModel(IServerAdminController admin, IConsoleInteraction interaction)
    {
        _admin = admin;
        _interaction = interaction;
        _admin.ConfigChanged += (_, _) => Dispatcher.UIThread.Post(RefreshLists);
        _admin.PlayersChanged += (_, _) => Dispatcher.UIThread.Post(RefreshStaff);
        LoadConfig();
        RefreshLists();
        RefreshStaff();
    }

    /// <summary>
    /// Areas players can move between
    /// </summary>
    public ObservableCollection<NamedItemViewModel> Areas { get; } = new();

    /// <summary>
    /// Music tracks offered to players
    /// </summary>
    public ObservableCollection<NamedItemViewModel> Music { get; } = new();

    /// <summary>
    /// Character roster override
    /// </summary>
    public ObservableCollection<NamedItemViewModel> Characters { get; } = new();

    /// <summary>
    /// Connected players currently holding moderator powers
    /// </summary>
    public ObservableCollection<PlayerItemViewModel> Staff { get; } = new();

    [RelayCommand]
    private void SelectTab(string tab)
    {
        IsGeneralTab = tab == "general";
        IsAreasTab = tab == "areas";
        IsMusicTab = tab == "music";
        IsCharactersTab = tab == "characters";
        IsStaffTab = tab == "staff";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!int.TryParse(PortText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
        {
            _interaction.ShowToast("Port must be between 1 and 65535", ToastSeverity.Error);
            return;
        }
        if (!int.TryParse(AuthPortText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var authPort) ||
            authPort is < 1 or > 65535)
        {
            _interaction.ShowToast("Auth port must be between 1 and 65535", ToastSeverity.Error);
            return;
        }
        if (!int.TryParse(HeartbeatText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var heartbeat) ||
            heartbeat < 1)
        {
            _interaction.ShowToast("Heartbeat must be a positive number of seconds", ToastSeverity.Error);
            return;
        }

        var current = _admin.GetConfig();
        await _admin.ApplyConfigAsync(current with
        {
            Name = ServerName.Trim(),
            ListenPort = port,
            IsPublic = IsPublic,
            HeartbeatSeconds = heartbeat,
            ModeratorPassword = ModeratorPassword,
            AuthServerHost = AuthHost.Trim(),
            AuthServerPort = authPort,
            AuthUseTls = AuthUseTls,
        }).ConfigureAwait(true);
        _interaction.ShowToast("Configuration saved", ToastSeverity.Success);
    }

    [RelayCommand]
    private async Task AddAreaAsync()
    {
        if (NewArea.Trim().Length == 0)
        {
            return;
        }
        await _admin.AddAreaAsync(NewArea).ConfigureAwait(true);
        NewArea = string.Empty;
        _interaction.ShowToast("Area added", ToastSeverity.Success);
    }

    [RelayCommand]
    private async Task AddMusicAsync()
    {
        if (NewMusic.Trim().Length == 0)
        {
            return;
        }
        await _admin.AddMusicAsync(NewMusic).ConfigureAwait(true);
        NewMusic = string.Empty;
        _interaction.ShowToast("Track added", ToastSeverity.Success);
    }

    [RelayCommand]
    private async Task AddCharacterAsync()
    {
        if (NewCharacter.Trim().Length == 0)
        {
            return;
        }
        await _admin.AddCharacterAsync(NewCharacter).ConfigureAwait(true);
        NewCharacter = string.Empty;
        _interaction.ShowToast("Character added", ToastSeverity.Success);
    }

    [RelayCommand]
    private async Task RevokeStaffAsync(PlayerItemViewModel player)
    {
        await _admin.SetModeratorAsync(player.Id, granted: false).ConfigureAwait(true);
        _interaction.ShowToast($"{player.Name} lost moderator powers", ToastSeverity.Success);
    }

    private void LoadConfig()
    {
        var config = _admin.GetConfig();
        ServerName = config.Name;
        PortText = config.ListenPort.ToString(CultureInfo.InvariantCulture);
        IsPublic = config.IsPublic;
        HeartbeatText = config.HeartbeatSeconds.ToString(CultureInfo.InvariantCulture);
        ModeratorPassword = config.ModeratorPassword;
        AuthHost = config.AuthServerHost;
        AuthPortText = config.AuthServerPort.ToString(CultureInfo.InvariantCulture);
        AuthUseTls = config.AuthUseTls;
    }

    private void RefreshLists()
    {
        Rebuild(Areas, _admin.GetAreas(), name => _ = RemoveAsync(_admin.RemoveAreaAsync(name), "Area removed"));
        Rebuild(Music, _admin.GetMusic(), name => _ = RemoveAsync(_admin.RemoveMusicAsync(name), "Track removed"));
        Rebuild(Characters, _admin.GetCharacters(),
            name => _ = RemoveAsync(_admin.RemoveCharacterAsync(name), "Character removed"));
        HasCharacters = Characters.Count > 0;
    }

    private void RefreshStaff()
    {
        Staff.Clear();
        foreach (var player in _admin.GetPlayers().Where(p => p.IsModerator))
        {
            Staff.Add(new PlayerItemViewModel(player));
        }
        HasStaff = Staff.Count > 0;
    }

    private async Task RemoveAsync(Task removal, string toast)
    {
        await removal.ConfigureAwait(true);
        _interaction.ShowToast(toast);
    }

    private static void Rebuild(
        ObservableCollection<NamedItemViewModel> target,
        System.Collections.Generic.IReadOnlyList<string> names,
        Action<string> remove)
    {
        target.Clear();
        foreach (var name in names)
        {
            target.Add(new NamedItemViewModel(name, remove));
        }
    }
}
