using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VNO.Core.Models;

namespace VNO.Server.Services;

/// <summary>
/// Thread-safe ban registry persisted atomically in the server data directory
/// </summary>
/// <remarks>
/// A parameterless instance remains in-memory for isolated tests. The application
/// supplies settings and persists every change to <c>bans.json</c>.
/// </remarks>
public sealed class BanRegistry : IBanRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    // keyed by kind and target so a lookup is a single dictionary hit
    private readonly ConcurrentDictionary<string, BanEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _persistenceGate = new();
    private readonly string? _path;

    /// <summary>
    /// Creates an in-memory registry
    /// </summary>
    public BanRegistry()
    {
    }

    /// <summary>
    /// Creates a registry backed by the configured server data directory
    /// </summary>
    public BanRegistry(IOptions<ServerSettings> settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Value.DataDirectory))
        {
            return;
        }

        Directory.CreateDirectory(settings.Value.DataDirectory);
        _path = Path.Combine(settings.Value.DataDirectory, "bans.json");
        Load();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<BanEntry> Entries => _entries.Values.ToList();

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void Add(BanEntry entry)
    {
        _entries[KeyOf(entry.Kind, entry.Target)] = entry;
        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public bool Remove(BanKind kind, string target)
    {
        var removed = _entries.TryRemove(KeyOf(kind, target), out _);
        if (removed)
        {
            Persist();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        return removed;
    }

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
        Persist();
        return false;
    }

    private void Load()
    {
        if (_path is null || !File.Exists(_path))
        {
            return;
        }

        var entries = JsonSerializer.Deserialize<List<BanEntry>>(File.ReadAllText(_path)) ?? [];
        foreach (var entry in entries.Where(entry =>
            !string.IsNullOrWhiteSpace(entry.Target) && entry.IsActiveAt(DateTimeOffset.UtcNow)))
        {
            _entries[KeyOf(entry.Kind, entry.Target)] = entry;
        }
    }

    private void Persist()
    {
        if (_path is null)
        {
            return;
        }

        lock (_persistenceGate)
        {
            var temporary = _path + ".tmp";
            var entries = _entries.Values
                .Where(entry => entry.IsActiveAt(DateTimeOffset.UtcNow))
                .OrderBy(entry => entry.Kind)
                .ThenBy(entry => entry.Target, StringComparer.OrdinalIgnoreCase)
                .ToList();
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }
    }

    private static string KeyOf(BanKind kind, string target) => $"{kind}:{target}";
}
