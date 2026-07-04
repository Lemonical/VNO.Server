using System;
using CommunityToolkit.Mvvm.Input;
using VNO.Server.Services;

namespace VNO.Server.ViewModels;

/// <summary>
/// One transient toast in the top right stack
/// </summary>
public sealed partial class ToastViewModel
{
    private readonly Action<ToastViewModel> _dismiss;

    /// <summary>
    /// Creates the toast with its dismiss action
    /// </summary>
    public ToastViewModel(string message, ToastSeverity severity, Action<ToastViewModel> dismiss)
    {
        Message = message;
        IsSuccess = severity == ToastSeverity.Success;
        IsError = severity == ToastSeverity.Error;
        _dismiss = dismiss;
    }

    /// <summary>
    /// Text shown in the toast
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// True when styled as success
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// True when styled as an error
    /// </summary>
    public bool IsError { get; }

    [RelayCommand]
    private void Dismiss() => _dismiss(this);
}
