using System;
using System.Collections.Generic;
using System.IO;
using VNO.Core.Networking;

namespace VNO.Server.Services;

/// <summary>
/// Builds <see cref="ServerSettings"/> from the legacy server data files
/// </summary>
/// <remarks>
/// The legacy server (Form3) read every setting from external files it let the
/// operator edit in place, init.ini for the host identity and auth link, areas.ini
/// for the room list, and musiclist.txt for the track list. This loader reproduces
/// that layout so the port is driven by the same files an operator already knows,
/// not an appsettings.json. Missing files fall back to the built in defaults, the
/// legacy tolerated absent data the same way
/// </remarks>
public static class ServerSettingsLoader
{
    // the data folder sits next to the executable and anchors every read
    private const string DataDirectoryName = "data";

    /// <summary>
    /// Reads the server data files from the data folder next to the executable
    /// </summary>
    public static ServerSettings Load()
    {
        var configured = Environment.GetEnvironmentVariable("VNO_DATA_DIRECTORY");
        return string.IsNullOrWhiteSpace(configured)
            ? Load(AppContext.BaseDirectory)
            : LoadDataDirectory(Path.GetFullPath(configured));
    }

    /// <summary>
    /// Reads the server data files from the data folder under the given base directory
    /// </summary>
    public static ServerSettings Load(string baseDirectory)
        => LoadDataDirectory(Path.Combine(baseDirectory, DataDirectoryName));

    private static ServerSettings LoadDataDirectory(string dataDirectory)
    {
        var settings = new ServerSettings { DataDirectory = dataDirectory };

        // init.ini carries the host identity, the public flag, and the auth link
        var init = DelphiIniFile.Load(Path.Combine(dataDirectory, "init.ini"));
        settings.Name = init.ReadString("Server", "name", settings.Name);
        settings.ListenPort = init.ReadInteger("Server", "port", settings.ListenPort);
        settings.ListenTransport = ReadTransport(init.ReadString("Server", "transport", string.Empty), settings.ListenTransport);
        settings.IsPublic = init.ReadBool("Server", "public", settings.IsPublic);
        settings.HeartbeatSeconds = init.ReadInteger("Server", "heartbeat", settings.HeartbeatSeconds);
        settings.ModeratorPassword = init.ReadString("Server", "moderatorpassword", settings.ModeratorPassword);
        settings.ChatBurst = init.ReadInteger("Server", "chatburst", settings.ChatBurst);
        settings.ChatMessagesPerSecond = ReadDouble(
            init.ReadString("Server", "chatrate", string.Empty), settings.ChatMessagesPerSecond);
        settings.AuthServerHost = init.ReadString("AS", "host", settings.AuthServerHost);
        settings.AuthServerPort = init.ReadInteger("AS", "port", settings.AuthServerPort);
        settings.AuthTransport = ReadTransport(init.ReadString("AS", "transport", string.Empty), settings.AuthTransport);
        settings.AuthUseTls = init.ReadBool("AS", "tls", settings.AuthUseTls);
        settings.AuthUsername = init.ReadString("AS", "username", settings.AuthUsername);
        settings.AuthPassword = init.ReadString("AS", "password", settings.AuthPassword);
        settings.AuthRemember = init.ReadBool("AS", "remember", settings.AuthRemember);

        // areas.ini names one area per [Section], musiclist and charlist are plain
        // one item per line text files, the same shapes the legacy editors saved
        var areas = ReadSectionNames(Path.Combine(dataDirectory, "areas.ini"));
        if (areas.Count > 0)
        {
            settings.Areas = areas;
        }

        var music = ReadLines(Path.Combine(dataDirectory, "musiclist.txt"));
        if (music.Count > 0)
        {
            settings.Music = music;
        }

        var characters = ReadLines(Path.Combine(dataDirectory, "charlist.txt"));
        if (characters.Count > 0)
        {
            settings.Characters = characters;
        }

        var items = ReadLines(Path.Combine(dataDirectory, "itemlist.txt"));
        if (items.Count > 0)
        {
            settings.Items = items;
        }

        ApplyEnvironment(settings);
        return settings;
    }

    private static void ApplyEnvironment(ServerSettings settings)
    {
        settings.Name = ReadEnvironment("VNO_SERVER_NAME", settings.Name);
        settings.ListenPort = ReadEnvironmentInteger("VNO_SERVER_PORT", settings.ListenPort);
        settings.ListenTransport = ReadTransport(
            ReadEnvironment("VNO_SERVER_TRANSPORT", string.Empty),
            settings.ListenTransport);
        settings.IsPublic = ReadEnvironmentBool("VNO_SERVER_PUBLIC", settings.IsPublic);
        settings.AuthServerHost = ReadEnvironment("VNO_AUTH_HOST", settings.AuthServerHost);
        settings.AuthServerPort = ReadEnvironmentInteger("VNO_AUTH_PORT", settings.AuthServerPort);
        settings.AuthTransport = ReadTransport(
            ReadEnvironment("VNO_AUTH_TRANSPORT", string.Empty),
            settings.AuthTransport);
        settings.AuthUseTls = ReadEnvironmentBool("VNO_AUTH_TLS", settings.AuthUseTls);
        settings.AuthUsername = ReadEnvironment("VNO_AUTH_USERNAME", settings.AuthUsername);
        settings.AuthRemember = ReadEnvironmentBool("VNO_AUTH_REMEMBER", settings.AuthRemember);

        var passwordFile = Environment.GetEnvironmentVariable("VNO_AUTH_PASSWORD_FILE");
        if (!string.IsNullOrWhiteSpace(passwordFile) && File.Exists(passwordFile))
        {
            settings.AuthPassword = File.ReadAllText(passwordFile).Trim();
            settings.AuthPasswordFromExternalSecret = true;
        }
    }

    private static string ReadEnvironment(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

    private static int ReadEnvironmentInteger(string name, int fallback) =>
        int.TryParse(
            Environment.GetEnvironmentVariable(name),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : fallback;

    private static bool ReadEnvironmentBool(string name, bool fallback) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

    // parse a decimal rate under the invariant culture, keep the fallback on anything unparseable
    private static double ReadDouble(string value, double fallback) =>
        double.TryParse(value.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    // accept websocket or ws for the WebSocket transport, anything else keeps the fallback
    private static Transport ReadTransport(string value, Transport fallback) =>
        value.Trim().ToLowerInvariant() switch
        {
            "websocket" or "ws" or "wss" => Transport.WebSocket,
            "tcp" => Transport.Tcp,
            _ => fallback,
        };

    private static List<string> ReadSectionNames(string path)
    {
        var ini = DelphiIniFile.Load(path);
        return new List<string>(ini.SectionNames);
    }

    // read a one item per line list, skipping blanks and the legacy comment forms
    private static List<string> ReadLines(string path)
    {
        var result = new List<string>();
        string[] lines;
        try
        {
            if (!File.Exists(path))
            {
                return result;
            }
            lines = File.ReadAllLines(path);
        }
        catch (IOException)
        {
            return result;
        }
        catch (UnauthorizedAccessException)
        {
            return result;
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) ||
                line.StartsWith(@"\\", StringComparison.Ordinal) || line.StartsWith(';'))
            {
                continue;
            }
            result.Add(line);
        }

        return result;
    }
}
