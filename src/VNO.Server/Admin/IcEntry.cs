using System;

namespace VNO.Server.Admin;

/// <summary>
/// One in character line for the console chat monitor
/// </summary>
public sealed record IcEntry(DateTimeOffset Timestamp, string Character, string Player, string Text);
