using System;
using System.Collections.Generic;
using System.IO;

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
    public static ServerSettings Load() => Load(AppContext.BaseDirectory);

    /// <summary>
    /// Reads the server data files from the data folder under the given base directory
    /// </summary>
    public static ServerSettings Load(string baseDirectory)
    {
        var settings = new ServerSettings();
        var dataDirectory = Path.Combine(baseDirectory, DataDirectoryName);

        // init.ini carries the host identity, the public flag, and the auth link
        var init = DelphiIniFile.Load(Path.Combine(dataDirectory, "init.ini"));
        settings.Name = init.ReadString("Server", "name", settings.Name);
        settings.ListenPort = init.ReadInteger("Server", "port", settings.ListenPort);
        settings.IsPublic = init.ReadBool("Server", "public", settings.IsPublic);
        settings.HeartbeatSeconds = init.ReadInteger("Server", "heartbeat", settings.HeartbeatSeconds);
        settings.ModeratorPassword = init.ReadString("Server", "moderatorpassword", settings.ModeratorPassword);
        settings.AuthServerHost = init.ReadString("AS", "host", settings.AuthServerHost);
        settings.AuthServerPort = init.ReadInteger("AS", "port", settings.AuthServerPort);

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

        return settings;
    }

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
