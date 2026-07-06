using System.Threading;
using System.Threading.Tasks;
using VNO.Server.Admin;

namespace VNO.Server.Services;

/// <summary>
/// Default sign in flow over the admin controller and the console interaction
/// </summary>
/// <remarks>
/// Hosting requires an auth server account, so the flow blocks the console with
/// a modal that only offers sign in. When the auth server itself is down the
/// operator can only close the app and edit the data files, which is the
/// intended behavior
/// </remarks>
public sealed class AuthLoginFlow : IAuthLoginFlow
{
    private readonly IServerAdminController _admin;
    private readonly IConsoleInteraction _interaction;
    private int _running;

    /// <summary>
    /// Creates the flow over the running services
    /// </summary>
    public AuthLoginFlow(IServerAdminController admin, IConsoleInteraction interaction)
    {
        _admin = admin;
        _interaction = interaction;
    }

    /// <inheritdoc />
    public async Task<bool> SignInAsync(CancellationToken cancellationToken = default)
    {
        // one sign in at a time, a second caller just yields to the running one
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            var saved = _admin.GetAuthCredentials();
            var username = saved.Username;
            var remember = saved.Remember;

            if (remember && saved.Username.Length > 0 && saved.Password.Length > 0)
            {
                var result = await _admin.ConnectAuthAsync(saved.Username, saved.Password, cancellationToken)
                    .ConfigureAwait(false);
                if (result == AuthConnectResult.Granted)
                {
                    _interaction.ShowToast($"Signed in to the auth server as {saved.Username}", ToastSeverity.Success);
                    return true;
                }
                _interaction.ShowToast(Describe(result), ToastSeverity.Error);
            }

            while (true)
            {
                var entry = await _interaction.ShowModalAsync(new ModalRequest(
                    "Auth Server Sign In",
                    "Hosting a server requires a VNO account. If the auth server cannot be reached, close the app and check data/init.ini.",
                    "Sign In",
                    IsDestructive: false,
                    ShowCredentials: true,
                    ShowRemember: true,
                    AllowCancel: false,
                    InitialUsername: username,
                    InitialRemember: remember)).ConfigureAwait(false);
                if (entry is null)
                {
                    // nothing rendered the modal, a headless caller cannot sign in
                    return false;
                }

                username = entry.Username;
                remember = entry.Remember;
                var result = await _admin.ConnectAuthAsync(entry.Username, entry.Password, cancellationToken)
                    .ConfigureAwait(false);
                if (result == AuthConnectResult.Granted)
                {
                    await _admin.SaveAuthCredentialsAsync(
                        new AuthCredentials(entry.Username, entry.Password, remember), cancellationToken)
                        .ConfigureAwait(false);
                    _interaction.ShowToast($"Signed in to the auth server as {entry.Username}", ToastSeverity.Success);
                    return true;
                }
                _interaction.ShowToast(Describe(result), ToastSeverity.Error);
            }
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>
    /// One line an operator can act on for each sign in outcome
    /// </summary>
    public static string Describe(AuthConnectResult result) => result switch
    {
        AuthConnectResult.Granted => "Connected to the auth server",
        AuthConnectResult.Unreachable => "Could not reach the auth server, check the AS section of data/init.ini",
        AuthConnectResult.VersionRejected => "The auth server rejected this server version",
        AuthConnectResult.Denied => "Wrong account name or password",
        AuthConnectResult.Banned => "This account is banned from the auth server",
        AuthConnectResult.TimedOut => "The auth server did not answer in time",
        _ => "The auth server sign in failed",
    };
}
