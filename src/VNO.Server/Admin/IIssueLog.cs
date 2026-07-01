using System;
using System.Collections.Generic;

namespace VNO.Server.Admin;

/// <summary>
/// Collects the warnings and errors every service logs so the console can show them
/// </summary>
/// <remarks>
/// Sits in the logging pipeline as a provider, so any component that logs at
/// warning or above lands here without knowing the console exists
/// </remarks>
public interface IIssueLog
{
    /// <summary>
    /// Snapshot of the captured issues, oldest first
    /// </summary>
    IReadOnlyList<IssueEntry> Issues { get; }

    /// <summary>
    /// Raised when a new issue is captured
    /// </summary>
    event EventHandler<IssueEntry>? IssueRaised;
}
