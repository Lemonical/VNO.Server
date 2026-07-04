using System;
using System.Globalization;
using System.IO;
using VNO.Server.Theming;

namespace VNO.Server.Services;

/// <summary>
/// Default appearance store over data\console.ini
/// </summary>
public sealed class AppearanceStore : IAppearanceStore
{
    private readonly string _path;

    /// <summary>
    /// Creates a store rooted next to the executable
    /// </summary>
    public AppearanceStore() : this(AppContext.BaseDirectory)
    {
    }

    /// <summary>
    /// Creates a store rooted at the given base directory
    /// </summary>
    public AppearanceStore(string baseDirectory) =>
        _path = Path.Combine(baseDirectory, "data", "console.ini");

    /// <inheritdoc />
    public AppearanceState Load()
    {
        var ini = DelphiIniFile.Load(_path);
        var theme = ini.ReadString("Console", "theme", "dark") == "light"
            ? ConsoleThemeVariant.Light
            : ConsoleThemeVariant.Dark;
        var accent = ini.ReadInteger("Console", "accent", 0);
        var density = ini.ReadString("Console", "density", "comfortable") == "compact"
            ? ConsoleDensity.Compact
            : ConsoleDensity.Comfortable;
        return new AppearanceState(theme, accent, density);
    }

    /// <inheritdoc />
    public void Save(AppearanceState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, string.Create(CultureInfo.InvariantCulture,
                $"[Console]\ntheme={(state.Theme == ConsoleThemeVariant.Light ? "light" : "dark")}\naccent={state.AccentIndex}\ndensity={(state.Density == ConsoleDensity.Compact ? "compact" : "comfortable")}\n"));
        }
        catch (IOException)
        {
            // losing a look preference is not worth failing an action over
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
