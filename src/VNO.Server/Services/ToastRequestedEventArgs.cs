using System;

namespace VNO.Server.Services;

/// <summary>
/// Carries one toast to the shell
/// </summary>
public sealed class ToastRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Creates the args
    /// </summary>
    public ToastRequestedEventArgs(string message, ToastSeverity severity)
    {
        Message = message;
        Severity = severity;
    }

    /// <summary>
    /// Text shown in the toast
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// How the toast is styled
    /// </summary>
    public ToastSeverity Severity { get; }
}
