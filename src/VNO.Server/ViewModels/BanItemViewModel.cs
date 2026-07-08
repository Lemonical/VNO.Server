using System;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using VNO.Core.Models;

namespace VNO.Server.ViewModels;

/// <summary>
/// One ban row for the bans table
/// </summary>
public sealed partial class BanItemViewModel
{
    private readonly Action<BanItemViewModel> _remove;

    /// <summary>
    /// Wraps a ban entry with its remove action
    /// </summary>
    public BanItemViewModel(BanEntry entry, Action<BanItemViewModel> remove)
    {
        Entry = entry;
        _remove = remove;
        DateText = entry.PlacedAt.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        DurationText = FormatDuration(entry);
    }

    /// <summary>
    /// The underlying ban record
    /// </summary>
    public BanEntry Entry { get; }

    /// <summary>
    /// True for account bans, false for address bans
    /// </summary>
    public bool IsAccount => Entry.Kind == BanKind.Account;

    /// <summary>
    /// Short kind word for the table
    /// </summary>
    public string KindLabel => IsAccount ? "Account" : "IP";

    /// <summary>
    /// The banned name or address
    /// </summary>
    public string Target => Entry.Target;

    /// <summary>
    /// Reason recorded with the ban
    /// </summary>
    public string Reason => string.IsNullOrEmpty(Entry.Reason) ? "No reason" : Entry.Reason;

    /// <summary>
    /// Who placed the ban
    /// </summary>
    public string PlacedBy => string.IsNullOrEmpty(Entry.PlacedBy) ? "staff" : Entry.PlacedBy;

    /// <summary>
    /// Local date the ban was placed
    /// </summary>
    public string DateText { get; }

    /// <summary>
    /// How long the ban lasts
    /// </summary>
    public string DurationText { get; }

    [RelayCommand]
    private void Remove() => _remove(this);

    private static string FormatDuration(BanEntry entry)
    {
        if (entry.ExpiresAt is null)
        {
            return "Permanent";
        }

        var span = entry.ExpiresAt.Value - entry.PlacedAt;
        if (span.TotalDays >= 1)
        {
            var days = (int)Math.Round(span.TotalDays);
            return days == 1 ? "1 day" : $"{days} days";
        }
        if (span.TotalHours >= 1)
        {
            var hours = (int)Math.Round(span.TotalHours);
            return hours == 1 ? "1 hour" : $"{hours} hours";
        }
        var minutes = Math.Max(1, (int)Math.Round(span.TotalMinutes));
        return minutes == 1 ? "1 minute" : $"{minutes} minutes";
    }
}
