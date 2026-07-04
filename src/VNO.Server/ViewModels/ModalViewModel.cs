using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNO.Server.Services;

namespace VNO.Server.ViewModels;

/// <summary>
/// The confirmation dialog shown over the window
/// </summary>
public sealed partial class ModalViewModel : ViewModelBase
{
    private readonly TaskCompletionSource<ModalResult?> _completion;
    private readonly Action _closed;

    [ObservableProperty]
    private string _reason = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _selectedDuration = "Permanent";

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _remember;

    /// <summary>
    /// Creates the modal for one request
    /// </summary>
    public ModalViewModel(
        ModalRequest request, TaskCompletionSource<ModalResult?> completion, Action closed)
    {
        Request = request;
        _completion = completion;
        _closed = closed;
        _username = request.InitialUsername;
        _remember = request.InitialRemember;
    }

    /// <summary>
    /// What is being asked
    /// </summary>
    public ModalRequest Request { get; }

    /// <summary>
    /// The duration choices for ban modals
    /// </summary>
    public IReadOnlyList<string> Durations { get; } =
        new[] { "1 hour", "6 hours", "1 day", "7 days", "30 days", "Permanent" };

    [RelayCommand]
    private void Confirm()
    {
        // a credentials modal cannot be confirmed empty, it would only bounce
        // off the auth server anyway
        if (Request.ShowCredentials && (Username.Trim().Length == 0 || Password.Length == 0))
        {
            return;
        }
        _completion.TrySetResult(new ModalResult(
            Reason.Trim(), ParseDuration(SelectedDuration), Message.Trim(),
            Username.Trim(), Password, Remember));
        _closed();
    }

    [RelayCommand]
    private void Cancel()
    {
        // a blocking modal ignores escape and has no cancel button
        if (!Request.AllowCancel)
        {
            return;
        }
        _completion.TrySetResult(null);
        _closed();
    }

    private static TimeSpan? ParseDuration(string label) => label switch
    {
        "1 hour" => TimeSpan.FromHours(1),
        "6 hours" => TimeSpan.FromHours(6),
        "1 day" => TimeSpan.FromDays(1),
        "7 days" => TimeSpan.FromDays(7),
        "30 days" => TimeSpan.FromDays(30),
        _ => null,
    };
}
