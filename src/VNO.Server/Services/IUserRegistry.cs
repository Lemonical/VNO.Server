using System.Collections.Generic;
using VNO.Core.Models;

namespace VNO.Server.Services;

/// <summary>
/// Tracks the players currently connected to the server
/// </summary>
/// <remarks>
/// Maps the network session id to a <see cref="ChatUser"/>. The legacy server
/// kept this list in the admin window user list, here it is a service so the
/// moderation logic and the UI share one source of truth
/// </remarks>
public interface IUserRegistry
{
    /// <summary>
    /// Snapshot of all connected users
    /// </summary>
    IReadOnlyCollection<ChatUser> Users { get; }

    /// <summary>
    /// Adds a user for a session and returns the created record
    /// </summary>
    ChatUser Add(string sessionId, string ipAddress);

    /// <summary>
    /// Removes the user for a session, returns the removed record or null
    /// </summary>
    ChatUser? Remove(string sessionId);

    /// <summary>
    /// Finds the user for a session, returns null when not found
    /// </summary>
    ChatUser? FindBySession(string sessionId);

    /// <summary>
    /// Finds the session id for a user id, returns null when not found
    /// </summary>
    string? FindSessionByUserId(int userId);
}
