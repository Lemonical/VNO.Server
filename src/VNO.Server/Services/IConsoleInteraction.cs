using System;
using System.Threading.Tasks;

namespace VNO.Server.Services;

/// <summary>
/// Lets any part of the console raise toasts and confirmation modals
/// </summary>
/// <remarks>
/// Page view models call the methods, the window shell listens to the events and
/// renders. Keeping this behind an interface means page logic never references
/// the shell and can be exercised in tests with a fake
/// </remarks>
public interface IConsoleInteraction
{
    /// <summary>
    /// Raised when a toast should appear
    /// </summary>
    event EventHandler<ToastRequestedEventArgs>? ToastRequested;

    /// <summary>
    /// Raised when a modal should open
    /// </summary>
    event EventHandler<ModalRequestedEventArgs>? ModalRequested;

    /// <summary>
    /// Shows a transient toast
    /// </summary>
    void ShowToast(string message, ToastSeverity severity = ToastSeverity.Info);

    /// <summary>
    /// Opens a modal and waits for the operator, null when cancelled
    /// </summary>
    Task<ModalResult?> ShowModalAsync(ModalRequest request);
}
