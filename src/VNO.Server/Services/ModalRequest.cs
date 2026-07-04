namespace VNO.Server.Services;

/// <summary>
/// A confirmation the console asks the operator for before acting
/// </summary>
/// <remarks>
/// The shell renders this over the window. The optional fields drive which
/// inputs appear, a reason line for moderation, a duration picker for bans, a
/// message box for broadcasts, and account fields for the auth server sign in.
/// AllowCancel false makes the modal blocking, it can only be confirmed
/// </remarks>
public sealed record ModalRequest(
    string Title,
    string Description,
    string ConfirmLabel,
    bool IsDestructive,
    bool ShowReason = false,
    bool ShowDuration = false,
    bool ShowMessage = false,
    bool ShowCredentials = false,
    bool ShowRemember = false,
    bool AllowCancel = true,
    string InitialUsername = "",
    bool InitialRemember = false);
