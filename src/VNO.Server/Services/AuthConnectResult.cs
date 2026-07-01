namespace VNO.Server.Services;

/// <summary>
/// Outcome of one attempt to connect and sign in to the auth server
/// </summary>
public enum AuthConnectResult
{
    /// <summary>
    /// The handshake and account login succeeded
    /// </summary>
    Granted,

    /// <summary>
    /// The auth server could not be reached at all
    /// </summary>
    Unreachable,

    /// <summary>
    /// The auth server rejected this server version or banned the address
    /// </summary>
    VersionRejected,

    /// <summary>
    /// The account name or password was wrong
    /// </summary>
    Denied,

    /// <summary>
    /// The account is banned on the auth server
    /// </summary>
    Banned,

    /// <summary>
    /// The auth server accepted the connection but never answered
    /// </summary>
    TimedOut,
}
