using System;
using System.Threading.Tasks;

namespace VNO.Server.Services;

/// <summary>
/// Default interaction hub between page view models and the window shell
/// </summary>
public sealed class ConsoleInteraction : IConsoleInteraction
{
    /// <inheritdoc />
    public event EventHandler<ToastRequestedEventArgs>? ToastRequested;

    /// <inheritdoc />
    public event EventHandler<ModalRequestedEventArgs>? ModalRequested;

    /// <inheritdoc />
    public void ShowToast(string message, ToastSeverity severity = ToastSeverity.Info) =>
        ToastRequested?.Invoke(this, new ToastRequestedEventArgs(message, severity));

    /// <inheritdoc />
    public Task<ModalResult?> ShowModalAsync(ModalRequest request)
    {
        var completion = new TaskCompletionSource<ModalResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var listeners = ModalRequested;
        if (listeners is null)
        {
            // nothing can render the modal, treat it as cancelled so callers stay safe
            return Task.FromResult<ModalResult?>(null);
        }
        listeners.Invoke(this, new ModalRequestedEventArgs(request, completion));
        return completion.Task;
    }
}
