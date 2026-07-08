using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNO.Server.Admin;

namespace VNO.Server.ViewModels;

/// <summary>
/// View model for the chat monitors, OOC with a server voice, read only IC, and events
/// </summary>
public sealed partial class ChatViewModel : ViewModelBase
{
    private const int MaxRows = 300;

    private readonly IServerAdminController _admin;

    [ObservableProperty]
    private bool _isOocTab = true;

    [ObservableProperty]
    private bool _isIcTab;

    [ObservableProperty]
    private bool _isEventsTab;

    [ObservableProperty]
    private string _oocInput = string.Empty;

    /// <summary>
    /// Creates the chat page over the admin controller
    /// </summary>
    public ChatViewModel(IServerAdminController admin)
    {
        _admin = admin;

        foreach (var entry in _admin.GetOocHistory())
        {
            Append(OocMessages, new OocItemViewModel(entry));
        }
        foreach (var entry in _admin.GetIcHistory())
        {
            Append(IcMessages, new IcItemViewModel(entry));
        }
        foreach (var entry in _admin.GetEvents())
        {
            Append(Events, new EventItemViewModel(entry));
        }

        _admin.OocReceived += (_, entry) =>
            Dispatcher.UIThread.Post(() => Append(OocMessages, new OocItemViewModel(entry)));
        _admin.IcReceived += (_, entry) =>
            Dispatcher.UIThread.Post(() => Append(IcMessages, new IcItemViewModel(entry)));
        _admin.EventLogged += (_, entry) =>
            Dispatcher.UIThread.Post(() => Append(Events, new EventItemViewModel(entry)));
    }

    /// <summary>
    /// Out of character lines, oldest first
    /// </summary>
    public ObservableCollection<OocItemViewModel> OocMessages { get; } = new();

    /// <summary>
    /// In character lines, oldest first
    /// </summary>
    public ObservableCollection<IcItemViewModel> IcMessages { get; } = new();

    /// <summary>
    /// Event log lines, oldest first
    /// </summary>
    public ObservableCollection<EventItemViewModel> Events { get; } = new();

    [RelayCommand]
    private void SelectTab(string tab)
    {
        IsOocTab = tab == "ooc";
        IsIcTab = tab == "ic";
        IsEventsTab = tab == "events";
    }

    [RelayCommand]
    private async Task SendOocAsync()
    {
        var text = OocInput.Trim();
        if (text.Length == 0)
        {
            return;
        }
        OocInput = string.Empty;
        await _admin.SendOocAsync(text).ConfigureAwait(true);
    }

    private static void Append<T>(ObservableCollection<T> list, T item)
    {
        list.Add(item);
        while (list.Count > MaxRows)
        {
            list.RemoveAt(0);
        }
    }
}
