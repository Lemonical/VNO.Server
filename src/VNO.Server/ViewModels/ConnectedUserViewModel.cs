using VNO.Core.Models;

namespace VNO.Server.ViewModels;

/// <summary>
/// A read only view of a connected player for the admin user list
/// </summary>
public sealed class ConnectedUserViewModel
{
    /// <summary>
    /// Wraps a domain user
    /// </summary>
    public ConnectedUserViewModel(ChatUser user) => User = user;

    /// <summary>
    /// The underlying domain user
    /// </summary>
    public ChatUser User { get; }

    /// <summary>
    /// Player id used by moderation actions
    /// </summary>
    public int Id => User.Id;

    /// <summary>
    /// Label shown in the list, mirrors the legacy user list format
    /// </summary>
    public string Display => $"{User.DisplayLabel} ({User.IpAddress})" + (User.IsMuted ? " [muted]" : string.Empty);
}
