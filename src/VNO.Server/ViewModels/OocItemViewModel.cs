using System.Globalization;
using VNO.Server.Admin;

namespace VNO.Server.ViewModels;

/// <summary>
/// One out of character line ready for the chat monitor
/// </summary>
public sealed class OocItemViewModel
{
    /// <summary>
    /// Wraps a chat entry
    /// </summary>
    public OocItemViewModel(ChatEntry entry)
    {
        Sender = entry.Sender;
        Text = entry.Text;
        TimeText = entry.Timestamp.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        IsServer = entry.IsServer;
    }

    /// <summary>
    /// Who spoke
    /// </summary>
    public string Sender { get; }

    /// <summary>
    /// The line itself
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Local time of the line
    /// </summary>
    public string TimeText { get; }

    /// <summary>
    /// True when the console operator spoke as the server
    /// </summary>
    public bool IsServer { get; }
}
