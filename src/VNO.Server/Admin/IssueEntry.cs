using System;

namespace VNO.Server.Admin;

/// <summary>
/// One captured warning or error with its expandable detail
/// </summary>
/// <remarks>
/// The text is the log message an operator scans, the detail carries the source
/// category and exception text shown when the row is expanded
/// </remarks>
public sealed record IssueEntry(DateTimeOffset Timestamp, IssueLevel Level, string Text, string Detail);
