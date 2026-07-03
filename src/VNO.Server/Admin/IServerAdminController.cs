using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VNO.Core.Models;
using VNO.Server.Services;

namespace VNO.Server.Admin;

/// <summary>
/// Everything a server console can see and do, behind one UI free surface
/// </summary>
/// <remarks>
/// The Avalonia window and any future command line frontend both drive the server
/// through this controller, mirroring how the master's admin console sits on
/// IMasterAdminController. Snapshots are immutable copies and every event carries
/// plain data, so a frontend never touches live game state and no call here
/// assumes a UI thread
/// </remarks>
public interface IServerAdminController
{
    /// <summary>
    /// Raised when the host goes online or offline
    /// </summary>
    event EventHandler<ServerStatus>? StatusChanged;

    /// <summary>
    /// Raised when the auth server link state changes
    /// </summary>
    event EventHandler<ConnectionState>? AuthStateChanged;

    /// <summary>
    /// Raised when the connected player list changes in any way
    /// </summary>
    event EventHandler? PlayersChanged;

    /// <summary>
    /// Raised for every new event log line
    /// </summary>
    event EventHandler<ConsoleEvent>? EventLogged;

    /// <summary>
    /// Raised for every out of character line the server sees or sends
    /// </summary>
    event EventHandler<ChatEntry>? OocReceived;

    /// <summary>
    /// Raised for every in character line the server relays
    /// </summary>
    event EventHandler<IcEntry>? IcReceived;

    /// <summary>
    /// Raised when a warning or error is captured
    /// </summary>
    event EventHandler<IssueEntry>? IssueRaised;

    /// <summary>
    /// Raised when the ban list changes, from the console or from in game staff
    /// </summary>
    event EventHandler? BansChanged;

    /// <summary>
    /// Raised after a configuration change is applied
    /// </summary>
    event EventHandler? ConfigChanged;

    /// <summary>
    /// Current summary for the dashboard and status bar
    /// </summary>
    ServerOverview GetOverview();

    /// <summary>
    /// Snapshot of the connected players
    /// </summary>
    IReadOnlyList<PlayerSnapshot> GetPlayers();

    /// <summary>
    /// Recent event log lines, oldest first
    /// </summary>
    IReadOnlyList<ConsoleEvent> GetEvents();

    /// <summary>
    /// Recent out of character lines, oldest first
    /// </summary>
    IReadOnlyList<ChatEntry> GetOocHistory();

    /// <summary>
    /// Recent in character lines, oldest first
    /// </summary>
    IReadOnlyList<IcEntry> GetIcHistory();

    /// <summary>
    /// Captured warnings and errors, oldest first
    /// </summary>
    IReadOnlyList<IssueEntry> GetIssues();

    /// <summary>
    /// Snapshot of the active bans
    /// </summary>
    IReadOnlyList<BanEntry> GetBans();

    /// <summary>
    /// Starts listening for players
    /// </summary>
    Task StartServerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops listening and drops all players
    /// </summary>
    Task StopServerAsync();

    /// <summary>
    /// Connects and signs in to the auth server, returns what actually happened
    /// </summary>
    Task<AuthConnectResult> ConnectAuthAsync(
        string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the auth server link
    /// </summary>
    Task DisconnectAuthAsync();

    /// <summary>
    /// The auth server account saved in the data files, empty when none
    /// </summary>
    AuthCredentials GetAuthCredentials();

    /// <summary>
    /// Persists or clears the saved auth server account, remember false clears it
    /// </summary>
    Task SaveAuthCredentialsAsync(
        AuthCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kicks a player, they may reconnect
    /// </summary>
    Task KickAsync(int userId, string reason);

    /// <summary>
    /// Mutes or unmutes a player
    /// </summary>
    Task SetMutedAsync(int userId, bool muted);

    /// <summary>
    /// Bans a player account and disconnects them, a null duration never expires
    /// </summary>
    Task BanAccountAsync(int userId, string reason, TimeSpan? duration);

    /// <summary>
    /// Bans an address and disconnects matching players, a null duration never expires
    /// </summary>
    Task BanAddressAsync(string ipAddress, string reason, TimeSpan? duration);

    /// <summary>
    /// Drops a player without recording anything
    /// </summary>
    Task DisconnectAsync(int userId);

    /// <summary>
    /// Removes a ban, returns true when one matched
    /// </summary>
    bool RemoveBan(BanKind kind, string target);

    /// <summary>
    /// Grants or revokes live moderator powers on a connected player
    /// </summary>
    Task SetModeratorAsync(int userId, bool granted);

    /// <summary>
    /// Sends a notice to every player
    /// </summary>
    Task BroadcastNoticeAsync(string text);

    /// <summary>
    /// Sends an out of character line to every player as the server
    /// </summary>
    Task SendOocAsync(string text);

    /// <summary>
    /// The editable settings as they are now
    /// </summary>
    ServerConfig GetConfig();

    /// <summary>
    /// Applies edited settings and persists them to the data files
    /// </summary>
    Task ApplyConfigAsync(ServerConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Areas players can move between, the first is the default spawn
    /// </summary>
    IReadOnlyList<string> GetAreas();

    /// <summary>
    /// Music tracks offered to players
    /// </summary>
    IReadOnlyList<string> GetMusic();

    /// <summary>
    /// Character roster override, empty means clients use their local roster
    /// </summary>
    IReadOnlyList<string> GetCharacters();

    /// <summary>
    /// Adds an area and persists the list
    /// </summary>
    Task AddAreaAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an area and persists the list
    /// </summary>
    Task RemoveAreaAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a music track and persists the list
    /// </summary>
    Task AddMusicAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a music track and persists the list
    /// </summary>
    Task RemoveMusicAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a character to the roster override and persists the list
    /// </summary>
    Task AddCharacterAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a character from the roster override and persists the list
    /// </summary>
    Task RemoveCharacterAsync(string name, CancellationToken cancellationToken = default);
}
