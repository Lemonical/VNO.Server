namespace VNO.Server.Theming;

/// <summary>
/// The saved look of the console, theme, accent, and density together
/// </summary>
public sealed record AppearanceState(
    ConsoleThemeVariant Theme,
    int AccentIndex,
    ConsoleDensity Density)
{
    /// <summary>
    /// The default look, dark with the gold accent
    /// </summary>
    public static AppearanceState Default { get; } =
        new(ConsoleThemeVariant.Dark, 0, ConsoleDensity.Comfortable);
}
