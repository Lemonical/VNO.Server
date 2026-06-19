using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using VNO.Core.Models;

namespace VNO.Server.Services;

/// <summary>
/// Default in memory ban registry, safe for use from many threads
/// </summary>
/// <remarks>
/// A production build would persist these to a file or database. The interface
/// hides that choice so storage can change without touching callers
/// </remarks>
public sealed class BanRegistry : IBanRegistry
{
    // keyed by kind and target so a lookup is a single dictionary hit
    private readonly ConcurrentDictionary<string, BanEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IReadOnlyCollection<BanEntry> Entries => _entries.Values.ToList();

    /// <inheritdoc />
    public void Add(BanEntry entry) => _entries[KeyOf(entry.Kind, entry.Target)] = entry;

    /// <inheritdoc />
    public bool Remove(BanKind kind, string target) => _entries.TryRemove(KeyOf(kind, target), out _);

    /// <inheritdoc />
    public bool IsAccountBanned(string userName) => IsBanned(BanKind.Account, userName);

    /// <inheritdoc />
    public bool IsAddressBanned(string ipAddress) => IsBanned(BanKind.IpAddress, ipAddress);

    private bool IsBanned(BanKind kind, string target)
    {
        if (!_entries.TryGetValue(KeyOf(kind, target), out var entry))
        {
            return false;
        }

        if (entry.IsActiveAt(DateTimeOffset.UtcNow))
        {
            return true;
        }

        // clean up an expired ban as we notice it
        _entries.TryRemove(KeyOf(kind, target), out _);
        return false;
    }

    private static string KeyOf(BanKind kind, string target) => $"{kind}:{target}";
}
