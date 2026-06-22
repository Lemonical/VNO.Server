namespace VNO.Server.Services;

/// <summary>
/// One out of character line the server saw, for the admin monitor
/// </summary>
/// <remarks>
/// The wire relays OOC text with no author, but the server knows which connected
/// player sent it, which is the whole point of a server side monitor. A line the
/// operator sends carries the sender <c>Server</c>
/// </remarks>
public sealed record OocLine(string Sender, string Text);
