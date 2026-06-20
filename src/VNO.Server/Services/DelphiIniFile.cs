using System;
using System.Collections.Generic;
using System.IO;

namespace VNO.Server.Services;

/// <summary>
/// Minimal reader for the Delphi style ini files the legacy server shipped
/// </summary>
/// <remarks>
/// The legacy server config files (init.ini, areas.ini, items.ini) are plain
/// [Section] key=value text with free form comment lines. Windows ini lookups are
/// case insensitive and return the first occurrence of a duplicated key, so this
/// reader keeps both behaviors. A missing file yields an empty document so every
/// read falls back to its default, matching how the original tolerated absent
/// data files. This mirrors the client reader of the same name, the two apps
/// vendor VNO.Core separately so a small parser is duplicated rather than shared
/// </remarks>
public sealed class DelphiIniFile
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _sectionOrder = new();

    private DelphiIniFile()
    {
    }

    /// <summary>
    /// Loads an ini file, returning an empty document when the file is missing or unreadable
    /// </summary>
    public static DelphiIniFile Load(string path)
    {
        var ini = new DelphiIniFile();
        string[] lines;
        try
        {
            if (!File.Exists(path))
            {
                return ini;
            }
            lines = File.ReadAllLines(path);
        }
        catch (IOException)
        {
            return ini;
        }
        catch (UnauthorizedAccessException)
        {
            return ini;
        }

        Dictionary<string, string>? current = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) ||
                line.StartsWith(@"\\", StringComparison.Ordinal) || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var name = line[1..^1].Trim();
                if (!ini._sections.TryGetValue(name, out current))
                {
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    ini._sections[name] = current;
                    ini._sectionOrder.Add(name);
                }
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0 || current is null)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            // first occurrence wins, like GetPrivateProfileString
            current.TryAdd(key, value);
        }

        return ini;
    }

    /// <summary>
    /// Section names in the order they first appear, used for files that name one
    /// item per section such as areas.ini
    /// </summary>
    public IReadOnlyList<string> SectionNames => _sectionOrder;

    /// <summary>
    /// Reads a string value or the fallback when the section or key is absent
    /// </summary>
    public string ReadString(string section, string key, string fallback)
    {
        if (_sections.TryGetValue(section, out var values) &&
            values.TryGetValue(key, out var value) && value.Length > 0)
        {
            return value;
        }
        return fallback;
    }

    /// <summary>
    /// Reads an integer value or the fallback when absent or unparseable
    /// </summary>
    public int ReadInteger(string section, string key, int fallback)
    {
        var text = ReadString(section, key, string.Empty);
        return int.TryParse(text, out var value) ? value : fallback;
    }

    /// <summary>
    /// Reads a boolean value, accepting the 1, true, yes, and on forms
    /// </summary>
    public bool ReadBool(string section, string key, bool fallback)
    {
        var text = ReadString(section, key, string.Empty);
        if (text.Length == 0)
        {
            return fallback;
        }
        return text.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
