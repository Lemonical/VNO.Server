using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNO.Server.Admin;

namespace VNO.Server.ViewModels;

/// <summary>
/// One warning or error row with its expandable detail
/// </summary>
public sealed partial class IssueItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Wraps a captured issue
    /// </summary>
    public IssueItemViewModel(IssueEntry entry)
    {
        TimeText = entry.Timestamp.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        Text = entry.Text;
        Detail = entry.Detail;
        IsError = entry.Level == IssueLevel.Error;
        LevelLabel = IsError ? "Error" : "Warning";
    }

    /// <summary>
    /// Local time the issue was captured
    /// </summary>
    public string TimeText { get; }

    /// <summary>
    /// The log message
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Source detail shown when expanded
    /// </summary>
    public string Detail { get; }

    /// <summary>
    /// True for errors, false for warnings
    /// </summary>
    public bool IsError { get; }

    /// <summary>
    /// Severity word next to the row
    /// </summary>
    public string LevelLabel { get; }

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}
