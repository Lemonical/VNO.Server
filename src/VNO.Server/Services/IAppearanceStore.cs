using VNO.Server.Theming;

namespace VNO.Server.Services;

/// <summary>
/// Persists the console appearance so the look survives a restart
/// </summary>
/// <remarks>
/// Saved to data\console.ini next to the other operator editable files. This is
/// presentation state only, nothing here affects hosted players
/// </remarks>
public interface IAppearanceStore
{
    /// <summary>
    /// Loads the saved appearance, or the default when nothing was saved
    /// </summary>
    AppearanceState Load();

    /// <summary>
    /// Saves the appearance
    /// </summary>
    void Save(AppearanceState state);
}
