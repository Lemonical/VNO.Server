using System;

namespace VNO.Server.Admin;

/// <summary>
/// One out of character line for the console chat monitor
/// </summary>
public sealed record ChatEntry(DateTimeOffset Timestamp, string Sender, string Text, bool IsServer);
