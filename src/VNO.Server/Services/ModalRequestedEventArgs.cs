using System;
using System.Threading.Tasks;

namespace VNO.Server.Services;

/// <summary>
/// Carries one modal request and its pending answer to the shell
/// </summary>
public sealed class ModalRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Creates the args
    /// </summary>
    public ModalRequestedEventArgs(ModalRequest request, TaskCompletionSource<ModalResult?> completion)
    {
        Request = request;
        Completion = completion;
    }

    /// <summary>
    /// What to show
    /// </summary>
    public ModalRequest Request { get; }

    /// <summary>
    /// Completed by the shell with the answer, null when cancelled
    /// </summary>
    public TaskCompletionSource<ModalResult?> Completion { get; }
}
