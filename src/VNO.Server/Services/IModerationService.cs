using System;
using System.Threading.Tasks;
using VNO.Core.Models;

namespace VNO.Server.Services;

/// <summary>
/// Carries out staff actions such as kick, mute, and ban
/// </summary>
/// <remarks>
/// These actions are shared by the server admin window and by moderator commands
/// that arrive over the network, so they live in one service. The actions mirror
/// the buttons on the legacy Form1 and Form3
/// </remarks>
public interface IModerationService
{
    /// <summary>
    /// Kicks a player, they may reconnect
    /// </summary>
    Task KickAsync(int userId, string reason);

    /// <summary>
    /// Mutes a player so they cannot chat
    /// </summary>
    Task MuteAsync(int userId);

    /// <summary>
    /// Removes a mute from a player
    /// </summary>
    Task UnmuteAsync(int userId);

    /// <summary>
    /// Bans a player account and disconnects them, a null duration never expires
    /// </summary>
    Task BanAccountAsync(int userId, string reason, string placedBy, TimeSpan? duration = null);

    /// <summary>
    /// Removes a ban on an account name
    /// </summary>
    void UnbanAccount(string userName);

    /// <summary>
    /// Bans an address and disconnects matching players, a null duration never expires
    /// </summary>
    Task BanAddressAsync(string ipAddress, string reason, string placedBy, TimeSpan? duration = null);

    /// <summary>
    /// Removes a ban on an address
    /// </summary>
    void UnbanAddress(string ipAddress);
}
