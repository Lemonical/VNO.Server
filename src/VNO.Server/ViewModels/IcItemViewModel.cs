using System.Globalization;
using VNO.Server.Admin;

namespace VNO.Server.ViewModels;

/// <summary>
/// One in character line ready for the chat monitor
/// </summary>
public sealed class IcItemViewModel
{
    /// <summary>
    /// Wraps an in character entry
    /// </summary>
    public IcItemViewModel(IcEntry entry)
    {
        Character = entry.Character;
        Player = entry.Player;
        Text = entry.Text;
        TimeText = entry.Timestamp.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The speaking character
    /// </summary>
    public string Character { get; }

    /// <summary>
    /// The player behind the character
    /// </summary>
    public string Player { get; }

    /// <summary>
    /// The line itself
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Local time of the line
    /// </summary>
    public string TimeText { get; }
}
