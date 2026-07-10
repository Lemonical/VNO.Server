using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VNO.Server.Admin;

namespace VNO.Server.Cli;

/// <summary>
/// Launches the React Ink server console against the local admin endpoint
/// </summary>
public static class ServerInkConsoleLauncher
{
    /// <summary>
    /// Installs the console dependencies when needed and runs the client
    /// </summary>
    public static async Task<int> RunAsync(
        ServerAdminEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        var workspace = FindWorkspace(Environment.CurrentDirectory) ?? FindWorkspace(AppContext.BaseDirectory);
        if (workspace is null)
        {
            Console.Error.WriteLine("Could not locate clients/server-console from this checkout.");
            return 1;
        }

        if (!Directory.Exists(Path.Combine(workspace, "node_modules")))
        {
            Console.WriteLine("Installing the VNO Server Ink console dependencies...");
            var install = Start(NpmCommand, ["install", "--no-fund", "--no-audit"], workspace, null);
            await install.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (install.ExitCode != 0)
            {
                return install.ExitCode;
            }
        }

        try
        {
            using var console = Start(
                NpmCommand,
                ["start", "--", "--url", endpoint.LocalUrl, "--name", endpoint.Name],
                workspace,
                endpoint.Token);
            await console.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return console.ExitCode;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Node.js 18 or newer with npm is required for the Ink console.");
            return 1;
        }
    }

    private static Process Start(
        string fileName,
        string[] arguments,
        string workingDirectory,
        string? token)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        if (token is not null)
        {
            start.Environment["SERVER_ADMIN_TOKEN"] = token;
        }

        return Process.Start(start) ?? throw new InvalidOperationException("Failed to start the Ink console");
    }

    private static string? FindWorkspace(string start)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            var package = Path.Combine(directory.FullName, "clients", "server-console", "package.json");
            if (File.Exists(package))
            {
                return Path.GetDirectoryName(package);
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string NpmCommand => OperatingSystem.IsWindows() ? "npm.cmd" : "npm";
}
