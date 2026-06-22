using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using VNO.Core.Models;
using VNO.Server.Services;

namespace VNO.Server.ViewModels;

/// <summary>
/// View model for the server admin window
/// </summary>
/// <remarks>
/// Ports the controls of the legacy Form3, the user list, the event log, the
/// online and AS status, and the moderation buttons. All host events are
/// marshalled to the UI thread so the bound collections stay safe to touch
/// </remarks>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IGameHost _host;
    private readonly IModerationService _moderation;
    private readonly IAuthServerLink _authLink;
    private readonly IUserRegistry _users;

    [ObservableProperty]
    private ConnectedUserViewModel? _selectedUser;

    [ObservableProperty]
    private string _serverStatusText = "Server Status: OFFLINE";

    [ObservableProperty]
    private string _authStatusText = "AS Connection: OFFLINE";

    [ObservableProperty]
    private string _publicStatusText = "Public: FALSE";

    [ObservableProperty]
    private string _outgoingNotice = string.Empty;

    [ObservableProperty]
    private string _outgoingOoc = string.Empty;

    /// <summary>
    /// Creates the view model with its services
    /// </summary>
    public MainWindowViewModel(
        IGameHost host,
        IModerationService moderation,
        IAuthServerLink authLink,
        IUserRegistry users,
        IOptions<ServerSettings> settings)
    {
        _host = host;
        _moderation = moderation;
        _authLink = authLink;
        _users = users;

        PublicStatusText = $"Public: {(settings.Value.IsPublic ? "TRUE" : "FALSE")}";

        _host.StatusChanged += OnStatusChanged;
        _host.UsersChanged += OnUsersChanged;
        _host.LogEntry += OnLogEntry;
        _host.OocReceived += OnOocReceived;
        _authLink.StateChanged += OnAuthStateChanged;
    }

    /// <summary>
    /// Players currently connected
    /// </summary>
    public ObservableCollection<ConnectedUserViewModel> Users { get; } = new();

    /// <summary>
    /// Recent server events, newest at the bottom
    /// </summary>
    public ObservableCollection<string> EventLog { get; } = new();

    /// <summary>
    /// Out of character chat the server has seen, the read side of the monitor
    /// </summary>
    public ObservableCollection<string> OocFeed { get; } = new();

    [RelayCommand]
    private async Task StartServerAsync() => await _host.StartAsync().ConfigureAwait(false);

    [RelayCommand]
    private async Task StopServerAsync() => await _host.StopAsync().ConfigureAwait(false);

    [RelayCommand]
    private async Task ConnectAuthAsync() => await _authLink.ConnectAsync().ConfigureAwait(false);

    [RelayCommand]
    private async Task KickAsync()
    {
        if (SelectedUser is not null)
        {
            await _moderation.KickAsync(SelectedUser.Id, "Kicked by staff").ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task MuteAsync()
    {
        if (SelectedUser is not null)
        {
            await _moderation.MuteAsync(SelectedUser.Id).ConfigureAwait(false);
            RefreshUsers();
        }
    }

    [RelayCommand]
    private async Task UnmuteAsync()
    {
        if (SelectedUser is not null)
        {
            await _moderation.UnmuteAsync(SelectedUser.Id).ConfigureAwait(false);
            RefreshUsers();
        }
    }

    [RelayCommand]
    private async Task BanAsync()
    {
        if (SelectedUser is not null)
        {
            await _moderation.BanAccountAsync(SelectedUser.Id, "Banned by staff", "admin").ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task BanIpAsync()
    {
        if (SelectedUser is not null)
        {
            await _moderation.BanAddressAsync(SelectedUser.User.IpAddress, "Banned by staff", "admin")
                .ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task SendNoticeAsync()
    {
        if (!string.IsNullOrWhiteSpace(OutgoingNotice))
        {
            await _host.BroadcastNoticeAsync(OutgoingNotice).ConfigureAwait(false);
            OutgoingNotice = string.Empty;
        }
    }

    [RelayCommand]
    private async Task SendOocAsync()
    {
        if (!string.IsNullOrWhiteSpace(OutgoingOoc))
        {
            await _host.SendOocAsync(OutgoingOoc).ConfigureAwait(false);
            OutgoingOoc = string.Empty;
        }
    }

    private void OnOocReceived(object? sender, OocLine line) =>
        Dispatcher.UIThread.Post(() =>
        {
            OocFeed.Add($"{DateTimeOffset.Now:HH:mm:ss}  {line.Sender}: {line.Text}");
            while (OocFeed.Count > 500)
            {
                OocFeed.RemoveAt(0);
            }
        });

    private void OnStatusChanged(object? sender, ServerStatus status) =>
        Dispatcher.UIThread.Post(() =>
            ServerStatusText = status == ServerStatus.Online
                ? "Server Status: ONLINE"
                : "Server Status: OFFLINE");

    private void OnUsersChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshUsers);

    private void OnLogEntry(object? sender, string entry) =>
        Dispatcher.UIThread.Post(() =>
        {
            EventLog.Add($"{DateTimeOffset.Now:HH:mm:ss}  {entry}");
            while (EventLog.Count > 500)
            {
                EventLog.RemoveAt(0);
            }
        });

    private void OnAuthStateChanged(object? sender, ConnectionState state) =>
        Dispatcher.UIThread.Post(() =>
            AuthStatusText = $"AS Connection: {state.ToString().ToUpperInvariant()}");

    private void RefreshUsers()
    {
        Users.Clear();
        foreach (var user in _users.Users)
        {
            Users.Add(new ConnectedUserViewModel(user));
        }
    }
}
