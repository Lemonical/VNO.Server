using System.Collections.Generic;
using VNO.Core.Models;

namespace VNO.Server.Services;

/// <summary>
/// Holds account bans and address bans and answers ban checks
/// </summary>
/// <remarks>
/// Replaces the separate ban and ipban lists from the legacy server. Both kinds
/// live here so the moderation logic has one place to ask
/// </remarks>
public interface IBanRegistry
{
    /// <summary>
    /// Snapshot of all ban records
    /// </summary>
    IReadOnlyCollection<BanEntry> Entries { get; }

    /// <summary>
    /// Adds or replaces a ban
    /// </summary>
    void Add(BanEntry entry);

    /// <summary>
    /// Removes a ban that matches the kind and target, returns true when removed
    /// </summary>
    bool Remove(BanKind kind, string target);

    /// <summary>
    /// True when the account name is currently banned
    /// </summary>
    bool IsAccountBanned(string userName);

    /// <summary>
    /// True when the address is currently banned
    /// </summary>
    bool IsAddressBanned(string ipAddress);
}
