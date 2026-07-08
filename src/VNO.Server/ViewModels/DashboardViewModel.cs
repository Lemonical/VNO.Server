using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNO.Core.Models;
using VNO.Server.Admin;
using VNO.Server.Services;

namespace VNO.Server.ViewModels;

/// <summary>
/// View model for the dashboard, the overview card, stat tiles, issues, and activity
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private const int MaxRows = 200;

    private readonly IServerAdminController _admin;
    private readonly IConsoleInteraction _interaction;
    private readonly IAuthLoginFlow _authFlow;
    private readonly DispatcherTimer _uptimeTimer;

    [ObservableProperty]
    private string _serverName = string.Empty;

    [ObservableProperty]
    private string _portText = string.Empty;

    [ObservableProperty]
    private string _transportLabel = "TCP";

    [ObservableProperty]
    private string _uptimeText = "—";

    [ObservableProperty]
    private bool _isPublicListing;

    [ObservableProperty]
    private string _listingLabel = "Private";

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private string _statusWord = "Offline";

    [ObservableProperty]
    private string _serverToggleText = "Start Server";

    [ObservableProperty]
    private bool _isAuthConnected;

    [ObservableProperty]
    private string _authToggleText = "Connect AS";

    [ObservableProperty]
    private int _playerCount;

    [ObservableProperty]
    private string _peakText = "Peak today: 0";

    [ObservableProperty]
    private int _totalMessages;

    [ObservableProperty]
    private string _oocCountText = "0 OOC";

    [ObservableProperty]
    private string _icCountText = "0 IC";

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private int _warnCount;

    [ObservableProperty]
    private string _issueTotalText = "0 total";

    [ObservableProperty]
    private bool _hasIssues;

    /// <summary>
    /// Creates the dashboard over the admin controller
    /// </summary>
    public DashboardViewModel(
        IServerAdminController admin, IConsoleInteraction interaction, IAuthLoginFlow authFlow)
    {
        _admin = admin;
        _interaction = interaction;
        _authFlow = authFlow;

        _admin.StatusChanged += (_, _) => Post(Refresh);
        _admin.AuthStateChanged += (_, _) => Post(Refresh);
        _admin.PlayersChanged += (_, _) => Post(Refresh);
        _admin.ConfigChanged += (_, _) => Post(Refresh);
        _admin.OocReceived += (_, _) => Post(Refresh);
        _admin.IcReceived += (_, _) => Post(Refresh);
        _admin.EventLogged += (_, entry) => Post(() => AddEvent(entry));
        _admin.IssueRaised += (_, entry) => Post(() => AddIssue(entry));

        foreach (var entry in _admin.GetEvents())
        {
            AddEvent(entry);
        }
        foreach (var entry in _admin.GetIssues())
        {
            AddIssue(entry);
        }
        Refresh();

        _uptimeTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateUptime());
        _uptimeTimer.Start();
    }

    /// <summary>
    /// Captured warnings and errors, newest first
    /// </summary>
    public ObservableCollection<IssueItemViewModel> Issues { get; } = new();

    /// <summary>
    /// Recent activity, newest first
    /// </summary>
    public ObservableCollection<EventItemViewModel> Events { get; } = new();

    [RelayCommand]
    private async Task ToggleServerAsync()
    {
        if (IsOnline)
        {
            await _admin.StopServerAsync().ConfigureAwait(true);
            _interaction.ShowToast("Server stopped");
        }
        else
        {
            try
            {
                await _admin.StartServerAsync().ConfigureAwait(true);
                _interaction.ShowToast(
                    $"Server started on {TransportLabel} :{PortText}", ToastSeverity.Success);
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException or System.IO.IOException)
            {
                _interaction.ShowToast($"Failed to start: {ex.Message}", ToastSeverity.Error);
            }
        }
        Refresh();
    }

    [RelayCommand]
    private async Task ToggleAuthAsync()
    {
        if (IsAuthConnected)
        {
            await _admin.DisconnectAuthAsync().ConfigureAwait(true);
            _interaction.ShowToast("Disconnected from auth server");
        }
        else
        {
            // the flow signs in with the saved account or asks for one, and it
            // reports the real outcome instead of assuming success
            await _authFlow.SignInAsync().ConfigureAwait(true);
        }
        Refresh();
    }

    private void Refresh()
    {
        var overview = _admin.GetOverview();
        ServerName = overview.Name;
        PortText = overview.ListenPort.ToString(CultureInfo.InvariantCulture);
        TransportLabel = overview.TransportLabel;
        IsOnline = overview.Status == ServerStatus.Online;
        StatusWord = IsOnline ? "Online" : "Offline";
        ServerToggleText = IsOnline ? "Stop Server" : "Start Server";
        IsAuthConnected = overview.AuthState == ConnectionState.Connected;
        AuthToggleText = IsAuthConnected ? "Disconnect AS" : "Connect AS";
        IsPublicListing = overview.IsPublic;
        ListingLabel = overview.IsPublic ? "Public" : "Private";
        PlayerCount = overview.PlayerCount;
        PeakText = $"Peak today: {overview.PeakPlayers}";
        TotalMessages = overview.OocMessageCount + overview.IcMessageCount;
        OocCountText = $"{overview.OocMessageCount} OOC";
        IcCountText = $"{overview.IcMessageCount} IC";
        UpdateUptime();
    }

    private void UpdateUptime()
    {
        var uptime = _admin.GetOverview().Uptime;
        UptimeText = uptime == TimeSpan.Zero
            ? "—"
            : uptime.TotalHours >= 1
                ? $"{(int)uptime.TotalHours}h {uptime.Minutes:00}m"
                : $"{uptime.Minutes}m {uptime.Seconds:00}s";
    }

    private void AddEvent(EventItemViewModel item)
    {
        Events.Insert(0, item);
        while (Events.Count > MaxRows)
        {
            Events.RemoveAt(Events.Count - 1);
        }
    }

    private void AddEvent(ConsoleEvent entry) => AddEvent(new EventItemViewModel(entry));

    private void AddIssue(IssueEntry entry)
    {
        Issues.Insert(0, new IssueItemViewModel(entry));
        while (Issues.Count > MaxRows)
        {
            Issues.RemoveAt(Issues.Count - 1);
        }
        WarnCount = 0;
        ErrorCount = 0;
        foreach (var issue in Issues)
        {
            if (issue.IsError)
            {
                ErrorCount++;
            }
            else
            {
                WarnCount++;
            }
        }
        IssueTotalText = $"{Issues.Count} total";
        HasIssues = Issues.Count > 0;
    }

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);
}
