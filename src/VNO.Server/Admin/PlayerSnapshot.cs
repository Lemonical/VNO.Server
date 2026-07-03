using System;

namespace VNO.Server.Admin;

/// <summary>
/// A point in time view of one connected player for console frontends
/// </summary>
/// <remarks>
/// Frontends never see the live <see cref="VNO.Core.Models.ChatUser"/>, they get
/// this immutable copy so a redraw cannot race the game host mutating the user
/// </remarks>
public sealed record PlayerSnapshot(
    int Id,
    string Name,
    string Character,
    int AreaId,
    string AreaName,
    string IpAddress,
    bool IsModerator,
    bool IsMuted,
    DateTimeOffset ConnectedAt);
