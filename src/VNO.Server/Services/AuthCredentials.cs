namespace VNO.Server.Services;

/// <summary>
/// The auth server account an operator signs in with
/// </summary>
/// <remarks>
/// Remember controls whether the name and password are written back to
/// data\init.ini so the next start can sign in without prompting
/// </remarks>
public sealed record AuthCredentials(string Username, string Password, bool Remember);
