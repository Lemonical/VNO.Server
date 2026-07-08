using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using VNO.Server.Admin;

namespace VNO.Server.ViewModels;

/// <summary>
/// One connected player row for the players table and the detail pane
/// </summary>
public sealed partial class PlayerItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Wraps a player snapshot
    /// </summary>
    public PlayerItemViewModel(PlayerSnapshot snapshot)
    {
        Snapshot = snapshot;
        JoinedText = snapshot.ConnectedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The underlying snapshot
    /// </summary>
    public PlayerSnapshot Snapshot { get; }

    /// <summary>
    /// Player id used by moderation actions
    /// </summary>
    public int Id => Snapshot.Id;

    /// <summary>
    /// Display name
    /// </summary>
    public string Name => Snapshot.Name;

    /// <summary>
    /// Character in use, or a placeholder when none picked yet
    /// </summary>
    public string Character =>
        string.IsNullOrEmpty(Snapshot.Character) ? "—" : Snapshot.Character;

    /// <summary>
    /// Area the player is in
    /// </summary>
    public string AreaName => Snapshot.AreaName;

    /// <summary>
    /// Network address
    /// </summary>
    public string IpAddress => Snapshot.IpAddress;

    /// <summary>
    /// True when the player holds staff powers
    /// </summary>
    public bool IsModerator => Snapshot.IsModerator;

    /// <summary>
    /// Short role word for the table
    /// </summary>
    public string RoleLabel => Snapshot.IsModerator ? "Mod" : "Player";

    /// <summary>
    /// True when the player cannot chat
    /// </summary>
    public bool IsMuted => Snapshot.IsMuted;

    /// <summary>
    /// Status word next to the dot
    /// </summary>
    public string StatusLabel => Snapshot.IsMuted ? "Muted" : "Active";

    /// <summary>
    /// Local time the player connected
    /// </summary>
    public string JoinedText { get; }
}
