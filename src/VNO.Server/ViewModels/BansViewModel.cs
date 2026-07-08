using System;
using System.Linq;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VNO.Server.Admin;
using VNO.Server.Services;

namespace VNO.Server.ViewModels;

/// <summary>
/// View model for the bans table
/// </summary>
public sealed partial class BansViewModel : ViewModelBase
{
    private readonly IServerAdminController _admin;
    private readonly IConsoleInteraction _interaction;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private string _countText = "0 active bans";

    [ObservableProperty]
    private bool _hasBans;

    /// <summary>
    /// Creates the bans page over the admin controller
    /// </summary>
    public BansViewModel(IServerAdminController admin, IConsoleInteraction interaction)
    {
        _admin = admin;
        _interaction = interaction;
        _admin.BansChanged += (_, _) => Dispatcher.UIThread.Post(Rebuild);
        Rebuild();
    }

    /// <summary>
    /// Bans matching the search, newest first
    /// </summary>
    public ObservableCollection<BanItemViewModel> Bans { get; } = new();

    partial void OnQueryChanged(string value) => Rebuild();

    private void Rebuild()
    {
        var query = Query.Trim();
        Bans.Clear();
        foreach (var entry in _admin.GetBans()
                     .Where(b => query.Length == 0 ||
                         b.Target.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                         b.Reason.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            Bans.Add(new BanItemViewModel(entry, Remove));
        }
        CountText = $"{_admin.GetBans().Count} active bans";
        HasBans = Bans.Count > 0;
    }

    private void Remove(BanItemViewModel item)
    {
        if (_admin.RemoveBan(item.Entry.Kind, item.Entry.Target))
        {
            _interaction.ShowToast("Ban removed", ToastSeverity.Success);
        }
    }
}
