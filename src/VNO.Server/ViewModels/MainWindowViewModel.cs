using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNO.Core.Models;
using VNO.Core.Protocol;
using VNO.Server.Admin;
using VNO.Server.Services;
using VNO.Server.Theming;

namespace VNO.Server.ViewModels;

/// <summary>
/// The console window shell, sidebar navigation, header, status bar, toasts, and modals
/// </summary>
/// <remarks>
/// Replaces the legacy Form3 admin window with the sectioned console. Pages talk
/// to the admin controller, the shell only routes between them and renders the
/// overlays the interaction service asks for
/// </remarks>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IServerAdminController _admin;
    private readonly IConsoleInteraction _interaction;
    private readonly IThemeManager _themes;
    private readonly IAuthLoginFlow _authFlow;
    private readonly DispatcherTimer _uptimeTimer;

    /// <summary>
    /// Shared application version used by Client, Server, and Master.
    /// </summary>
    public string ApplicationVersionLabel { get; } = $"VNO Server v{ProtocolConstants.ApplicationVersion}";

    [ObservableProperty]
    private ConsoleSection _currentSection = ConsoleSection.Dashboard;

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private string _title = "Dashboard";

    [ObservableProperty]
    private bool _sidebarExpanded = true;

    [ObservableProperty]
    private double _sidebarWidth = 220;

    [ObservableProperty]
    private string _themeLabel = "Dark mode";

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private string _headerStatusText = "OFFLINE";

    [ObservableProperty]
    private string _statusLabel = "Offline";

    [ObservableProperty]
    private string _authLabel = "Disconnected";

    [ObservableProperty]
    private string _accountName = "Not signed in";

    [ObservableProperty]
    private string _playerCountText = "0 players";

    [ObservableProperty]
    private string _uptimeText = "Uptime: —";

    [ObservableProperty]
    private int _playerCount;

    [ObservableProperty]
    private int _banCount;

    [ObservableProperty]
    private bool _hasPlayers;

    [ObservableProperty]
    private bool _hasBans;

    [ObservableProperty]
    private ModalViewModel? _activeModal;

    /// <summary>
    /// Creates the shell with the pages and services it hosts
    /// </summary>
    public MainWindowViewModel(
        IServerAdminController admin,
        IConsoleInteraction interaction,
        IThemeManager themes,
        IAuthLoginFlow authFlow,
        DashboardViewModel dashboard,
        PlayersViewModel players,
        ChatViewModel chat,
        ConfigurationViewModel configuration,
        BansViewModel bans,
        AppearanceViewModel appearance)
    {
        _admin = admin;
        _interaction = interaction;
        _themes = themes;
        _authFlow = authFlow;
        Dashboard = dashboard;
        Players = players;
        Chat = chat;
        Configuration = configuration;
        Bans = bans;
        Appearance = appearance;
        _currentPage = dashboard;

        _interaction.ToastRequested += (_, e) =>
            Dispatcher.UIThread.Post(() => AddToast(e.Message, e.Severity));
        _interaction.ModalRequested += (_, e) =>
            Dispatcher.UIThread.Post(() =>
                ActiveModal = new ModalViewModel(e.Request, e.Completion, () => ActiveModal = null));

        _admin.StatusChanged += (_, _) => Dispatcher.UIThread.Post(RefreshStatus);
        _admin.AuthStateChanged += (_, _) => Dispatcher.UIThread.Post(RefreshStatus);
        _admin.PlayersChanged += (_, _) => Dispatcher.UIThread.Post(RefreshStatus);
        _admin.BansChanged += (_, _) => Dispatcher.UIThread.Post(RefreshStatus);
        _themes.Changed += (_, _) => Dispatcher.UIThread.Post(RefreshThemeLabel);

        RefreshStatus();
        RefreshThemeLabel();

        _uptimeTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateUptime());
        _uptimeTimer.Start();
    }

    /// <summary>
    /// The dashboard page
    /// </summary>
    public DashboardViewModel Dashboard { get; }

    /// <summary>
    /// The players page
    /// </summary>
    public PlayersViewModel Players { get; }

    /// <summary>
    /// The chat page
    /// </summary>
    public ChatViewModel Chat { get; }

    /// <summary>
    /// The configuration page
    /// </summary>
    public ConfigurationViewModel Configuration { get; }

    /// <summary>
    /// The bans page
    /// </summary>
    public BansViewModel Bans { get; }

    /// <summary>
    /// The appearance page
    /// </summary>
    public AppearanceViewModel Appearance { get; }

    /// <summary>
    /// Toasts stacked in the top right
    /// </summary>
    public ObservableCollection<ToastViewModel> Toasts { get; } = new();

    /// <summary>
    /// Runs the boot sequence once the window is up, the blocking auth server
    /// sign in that hosting an account gated server requires
    /// </summary>
    public async Task StartupAsync()
    {
        try
        {
            await _authFlow.SignInAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _interaction.ShowToast($"Auth server sign in failed: {ex.Message}", ToastSeverity.Error);
        }
    }

    /// <summary>
    /// True while a modal is open
    /// </summary>
    public bool IsModalOpen => ActiveModal is not null;

    /// <summary>
    /// True while the dashboard section is shown, drives the sidebar highlight
    /// </summary>
    public bool IsDashboardActive => CurrentSection == ConsoleSection.Dashboard;

    /// <summary>
    /// True while the players section is shown
    /// </summary>
    public bool IsPlayersActive => CurrentSection == ConsoleSection.Players;

    /// <summary>
    /// True while the chat section is shown
    /// </summary>
    public bool IsChatActive => CurrentSection == ConsoleSection.Chat;

    /// <summary>
    /// True while the configuration section is shown
    /// </summary>
    public bool IsConfigurationActive => CurrentSection == ConsoleSection.Configuration;

    /// <summary>
    /// True while the bans section is shown
    /// </summary>
    public bool IsBansActive => CurrentSection == ConsoleSection.Bans;

    /// <summary>
    /// True while the appearance section is shown
    /// </summary>
    public bool IsAppearanceActive => CurrentSection == ConsoleSection.Appearance;

    partial void OnActiveModalChanged(ModalViewModel? value) => OnPropertyChanged(nameof(IsModalOpen));

    partial void OnCurrentSectionChanged(ConsoleSection value)
    {
        CurrentPage = value switch
        {
            ConsoleSection.Players => Players,
            ConsoleSection.Chat => Chat,
            ConsoleSection.Configuration => Configuration,
            ConsoleSection.Bans => Bans,
            ConsoleSection.Appearance => Appearance,
            _ => Dashboard,
        };
        Title = value switch
        {
            ConsoleSection.Players => "Players",
            ConsoleSection.Chat => "Chat",
            ConsoleSection.Configuration => "Configuration",
            ConsoleSection.Bans => "Bans",
            ConsoleSection.Appearance => "Appearance",
            _ => "Dashboard",
        };
        OnPropertyChanged(nameof(IsDashboardActive));
        OnPropertyChanged(nameof(IsPlayersActive));
        OnPropertyChanged(nameof(IsChatActive));
        OnPropertyChanged(nameof(IsConfigurationActive));
        OnPropertyChanged(nameof(IsBansActive));
        OnPropertyChanged(nameof(IsAppearanceActive));
    }

    [RelayCommand]
    private void Navigate(string section)
    {
        CurrentSection = section switch
        {
            "players" => ConsoleSection.Players,
            "chat" => ConsoleSection.Chat,
            "configuration" => ConsoleSection.Configuration,
            "bans" => ConsoleSection.Bans,
            "appearance" => ConsoleSection.Appearance,
            _ => ConsoleSection.Dashboard,
        };
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        SidebarExpanded = !SidebarExpanded;
        SidebarWidth = SidebarExpanded ? 220 : 56;
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var dark = _themes.State.Theme == ConsoleThemeVariant.Dark;
        _themes.Apply(_themes.State with
        {
            Theme = dark ? ConsoleThemeVariant.Light : ConsoleThemeVariant.Dark,
        });
    }

    [RelayCommand]
    private async Task SendNoticeAsync()
    {
        var result = await _interaction.ShowModalAsync(new ModalRequest(
            "Send Broadcast Notice",
            "Send a notice to all connected players.",
            "Send",
            IsDestructive: false,
            ShowMessage: true)).ConfigureAwait(true);
        if (result is null || result.Message.Length == 0)
        {
            return;
        }
        await _admin.BroadcastNoticeAsync(result.Message).ConfigureAwait(true);
        _interaction.ShowToast("Broadcast sent to all players", ToastSeverity.Success);
    }

    private void AddToast(string message, ToastSeverity severity)
    {
        var toast = new ToastViewModel(message, severity, t => Toasts.Remove(t));
        Toasts.Add(toast);
        DispatcherTimer.RunOnce(() => Toasts.Remove(toast), TimeSpan.FromSeconds(3.5));
    }

    private void RefreshStatus()
    {
        var overview = _admin.GetOverview();
        IsOnline = overview.Status == ServerStatus.Online;
        HeaderStatusText = IsOnline ? "ONLINE" : "OFFLINE";
        StatusLabel = IsOnline ? "Online" : "Offline";
        AuthLabel = overview.AuthState == ConnectionState.Connected ? "Connected" : "Disconnected";
        AccountName = string.IsNullOrEmpty(overview.AuthUsername) ? "Not signed in" : overview.AuthUsername;
        PlayerCount = overview.PlayerCount;
        PlayerCountText = $"{overview.PlayerCount} players";
        HasPlayers = overview.PlayerCount > 0;
        BanCount = _admin.GetBans().Count;
        HasBans = BanCount > 0;
        UpdateUptime();
    }

    private void UpdateUptime()
    {
        var uptime = _admin.GetOverview().Uptime;
        UptimeText = uptime == TimeSpan.Zero
            ? "Uptime: —"
            : uptime.TotalHours >= 1
                ? $"Uptime: {(int)uptime.TotalHours}h {uptime.Minutes:00}m"
                : $"Uptime: {uptime.Minutes}m {uptime.Seconds:00}s";
    }

    private void RefreshThemeLabel() =>
        ThemeLabel = _themes.State.Theme == ConsoleThemeVariant.Dark ? "Dark mode" : "Light mode";
}
