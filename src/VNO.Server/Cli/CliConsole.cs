using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VNO.Core.Models;
using VNO.Server.Admin;
using VNO.Server.Services;

namespace VNO.Server.Cli;

/// <summary>
/// The windowless server console, started with --cli
/// </summary>
/// <remarks>
/// Drives the exact same <see cref="IServerAdminController"/> the Avalonia
/// window uses. It signs in to the auth server first, with the remembered
/// account or by prompting, because hosting requires an account. When the auth
/// server cannot be reached the process exits so the operator can fix the AS
/// section of data\init.ini, there is nothing else to do without it
/// </remarks>
public static class CliConsole
{
    /// <summary>
    /// Runs the console until the operator quits, returns the process exit code
    /// </summary>
    public static async Task<int> RunAsync(IServiceProvider services, bool headless = false)
    {
        var admin = services.GetRequiredService<IServerAdminController>();
        var endpoint = services.GetRequiredService<ServerAdminEndpoint>();
        var settings = services.GetRequiredService<IOptions<ServerSettings>>().Value;

        Console.WriteLine($"VNO Server console, auth server {settings.AuthServerHost}:{settings.AuthServerPort}");

        if (!await SignInAsync(admin).ConfigureAwait(false))
        {
            return 1;
        }

        await admin.StartServerAsync().ConfigureAwait(false);
        await endpoint.StartAsync().ConfigureAwait(false);

        EventHandler<ConsoleEvent>? headlessLogHandler = null;
        try
        {
            if (headless || Console.IsInputRedirected)
            {
                Console.WriteLine($"Hosting players on port {settings.ListenPort}");
                headlessLogHandler = (_, entry) =>
                    Console.WriteLine($"[{entry.Timestamp:HH:mm:ss}] {entry.Text}");
                admin.EventLogged += headlessLogHandler;
                await WaitForShutdownAsync().ConfigureAwait(false);
                return 0;
            }

            return await ServerInkConsoleLauncher.RunAsync(endpoint).ConfigureAwait(false);
        }
        finally
        {
            if (headlessLogHandler is not null)
            {
                admin.EventLogged -= headlessLogHandler;
            }

            await endpoint.DisposeAsync().ConfigureAwait(false);
            await admin.StopServerAsync().ConfigureAwait(false);
            await admin.DisconnectAuthAsync().ConfigureAwait(false);
        }
    }

    private static async Task WaitForShutdownAsync()
    {
        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            shutdown.TrySetResult();
        };
        Console.CancelKeyPress += cancelHandler;

        PosixSignalRegistration? terminate = null;
        if (!OperatingSystem.IsWindows())
        {
            terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                shutdown.TrySetResult();
            });
        }

        try
        {
            await shutdown.Task.ConfigureAwait(false);
        }
        finally
        {
            terminate?.Dispose();
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    // sign in with the remembered account first, then prompt. A wrong password
    // asks again, an unreachable or refusing auth server ends the process
    private static async Task<bool> SignInAsync(IServerAdminController admin)
    {
        var saved = admin.GetAuthCredentials();
        if (saved.Remember && saved.Username.Length > 0 && saved.Password.Length > 0)
        {
            Console.WriteLine($"Signing in as {saved.Username}...");
            var result = await admin.ConnectAuthAsync(saved.Username, saved.Password).ConfigureAwait(false);
            if (result == AuthConnectResult.Granted)
            {
                Console.WriteLine($"Signed in to the auth server as {saved.Username}");
                return true;
            }
            Console.WriteLine(AuthLoginFlow.Describe(result));
            if (IsFatal(result))
            {
                return false;
            }
        }

        while (true)
        {
            Console.Write("Username: ");
            var username = Console.ReadLine()?.Trim();
            if (username is null)
            {
                return false;
            }
            if (username.Length == 0)
            {
                continue;
            }

            var password = ReadPassword("Password: ");
            if (password.Length == 0)
            {
                continue;
            }

            Console.Write("Remember me? [y/N]: ");
            var remember = (Console.ReadLine()?.Trim().ToLowerInvariant() ?? "n") is "y" or "yes";

            var result = await admin.ConnectAuthAsync(username, password).ConfigureAwait(false);
            if (result == AuthConnectResult.Granted)
            {
                await admin.SaveAuthCredentialsAsync(new AuthCredentials(username, password, remember))
                    .ConfigureAwait(false);
                Console.WriteLine($"Signed in to the auth server as {username}");
                return true;
            }

            Console.WriteLine(AuthLoginFlow.Describe(result));
            if (IsFatal(result))
            {
                return false;
            }
        }
    }

    // without a reachable auth server, or with a refused version or a banned
    // account, the operator can only edit the data files and start again
    private static bool IsFatal(AuthConnectResult result) =>
        result is AuthConnectResult.Unreachable
            or AuthConnectResult.VersionRejected
            or AuthConnectResult.Banned
            or AuthConnectResult.TimedOut;

    // mask the password when a real terminal is attached, fall back to a plain
    // line when input is piped in
    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? string.Empty;
        }

        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }
}
