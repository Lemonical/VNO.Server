using System.Globalization;
using VNO.Server.Admin;

namespace VNO.Server.ViewModels;

/// <summary>
/// One event log line ready for display
/// </summary>
public sealed class EventItemViewModel
{
    /// <summary>
    /// Wraps a console event
    /// </summary>
    public EventItemViewModel(ConsoleEvent entry)
    {
        TimeText = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Text = entry.Text;
        IsJoin = entry.Kind == ConsoleEventKind.Join;
        IsModeration = entry.Kind == ConsoleEventKind.Moderation;
        IsError = entry.Kind == ConsoleEventKind.Error;
    }

    /// <summary>
    /// Local time the event happened
    /// </summary>
    public string TimeText { get; }

    /// <summary>
    /// The event text
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// True for connect and disconnect events
    /// </summary>
    public bool IsJoin { get; }

    /// <summary>
    /// True for staff actions
    /// </summary>
    public bool IsModeration { get; }

    /// <summary>
    /// True for failures
    /// </summary>
    public bool IsError { get; }
}
