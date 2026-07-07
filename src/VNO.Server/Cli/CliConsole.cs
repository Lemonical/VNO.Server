using System;
using System.Globalization;
using System.Text;
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
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        var admin = services.GetRequiredService<IServerAdminController>();
        var settings = services.GetRequiredService<IOptions<ServerSettings>>().Value;

        Console.WriteLine($"VNO Server console, auth server {settings.AuthServerHost}:{settings.AuthServerPort}");

        if (!await SignInAsync(admin).ConfigureAwait(false))
        {
            return 1;
        }

        await admin.StartServerAsync().ConfigureAwait(false);
        Console.WriteLine($"Hosting players on port {settings.ListenPort}, type help for commands");

        admin.EventLogged += (_, e) =>
            Console.WriteLine($"[{e.Timestamp:HH:mm:ss}] {e.Text}");

        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line is null)
            {
                break;
            }
            if (!await DispatchAsync(admin, line.Trim()).ConfigureAwait(false))
            {
                break;
            }
        }

        await admin.StopServerAsync().ConfigureAwait(false);
        await admin.DisconnectAuthAsync().ConfigureAwait(false);
        return 0;
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

    private static async Task<bool> DispatchAsync(IServerAdminController admin, string line)
    {
        if (line.Length == 0)
        {
            return true;
        }

        var space = line.IndexOf(' ', StringComparison.Ordinal);
        var verb = (space < 0 ? line : line[..space]).ToLowerInvariant();
        var rest = space < 0 ? string.Empty : line[(space + 1)..].Trim();

        switch (verb)
        {
            case "help":
                Console.WriteLine("""
                    status              server, auth link, players, uptime
                    players             list connected players
                    kick <id> [reason]  kick a player
                    notice <text>       broadcast a notice to everyone
                    ooc <text>          speak in out of character chat as Server
                    stop                stop hosting players
                    start               start hosting players again
                    quit                stop everything and exit
                    """);
                return true;

            case "status":
                PrintStatus(admin.GetOverview());
                return true;

            case "players":
                foreach (var player in admin.GetPlayers())
                {
                    Console.WriteLine(
                        $"{player.Id,4}  {player.Name}  {player.Character}  {player.AreaName}  {player.IpAddress}");
                }
                if (admin.GetPlayers().Count == 0)
                {
                    Console.WriteLine("No players connected");
                }
                return true;

            case "kick":
            {
                var reasonSplit = rest.Split(' ', 2);
                if (reasonSplit.Length == 0 ||
                    !int.TryParse(reasonSplit[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                {
                    Console.WriteLine("Usage: kick <id> [reason]");
                    return true;
                }
                await admin.KickAsync(id, reasonSplit.Length > 1 ? reasonSplit[1] : string.Empty)
                    .ConfigureAwait(false);
                return true;
            }

            case "notice" when rest.Length > 0:
                await admin.BroadcastNoticeAsync(rest).ConfigureAwait(false);
                return true;

            case "ooc" when rest.Length > 0:
                await admin.SendOocAsync(rest).ConfigureAwait(false);
                return true;

            case "stop":
                await admin.StopServerAsync().ConfigureAwait(false);
                Console.WriteLine("Server stopped");
                return true;

            case "start":
                await admin.StartServerAsync().ConfigureAwait(false);
                Console.WriteLine("Server started");
                return true;

            case "quit":
            case "exit":
                return false;

            default:
                Console.WriteLine("Unknown command, type help");
                return true;
        }
    }

    private static void PrintStatus(ServerOverview overview)
    {
        Console.WriteLine($"Server    {overview.Name} ({overview.TransportLabel} :{overview.ListenPort})");
        Console.WriteLine($"Status    {(overview.Status == ServerStatus.Online ? "Online" : "Offline")}");
        Console.WriteLine($"Auth      {(overview.AuthState == ConnectionState.Connected ? $"Connected as {overview.AuthUsername}" : "Disconnected")}");
        Console.WriteLine($"Players   {overview.PlayerCount} (peak {overview.PeakPlayers})");
        Console.WriteLine($"Messages  {overview.OocMessageCount} OOC, {overview.IcMessageCount} IC");
        Console.WriteLine($"Uptime    {overview.Uptime}");
    }

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
