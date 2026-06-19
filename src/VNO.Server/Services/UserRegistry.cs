using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using VNO.Core.Models;

namespace VNO.Server.Services;

/// <summary>
/// Default in memory user registry, safe for use from many threads
/// </summary>
public sealed class UserRegistry : IUserRegistry
{
    private readonly ConcurrentDictionary<string, ChatUser> _bySession = new();
    private int _nextUserId;

    /// <inheritdoc />
    public IReadOnlyCollection<ChatUser> Users => _bySession.Values.ToList();

    /// <inheritdoc />
    public ChatUser Add(string sessionId, string ipAddress)
    {
        var user = new ChatUser
        {
            Id = Interlocked.Increment(ref _nextUserId),
            IpAddress = ipAddress,
            Name = "Player",
        };

        _bySession[sessionId] = user;
        return user;
    }

    /// <inheritdoc />
    public ChatUser? Remove(string sessionId) =>
        _bySession.TryRemove(sessionId, out var user) ? user : null;

    /// <inheritdoc />
    public ChatUser? FindBySession(string sessionId) =>
        _bySession.TryGetValue(sessionId, out var user) ? user : null;

    /// <inheritdoc />
    public string? FindSessionByUserId(int userId) =>
        _bySession.FirstOrDefault(pair => pair.Value.Id == userId).Key;
}
