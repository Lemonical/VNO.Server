using System;

namespace VNO.Server.Admin;

/// <summary>
/// One line of server activity for the console event log
/// </summary>
public sealed record ConsoleEvent(DateTimeOffset Timestamp, ConsoleEventKind Kind, string Text);
