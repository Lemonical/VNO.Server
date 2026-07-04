using System;
using System.Collections.Generic;

namespace VNO.Server.Theming;

/// <summary>
/// Applies the console palette and remembers the chosen look
/// </summary>
/// <remarks>
/// Owns the dynamic resources every view binds, so switching theme, accent, or
/// density restyles the whole window at once. Persists through the appearance
/// store so the look survives a restart
/// </remarks>
public interface IThemeManager
{
    /// <summary>
    /// The look currently applied
    /// </summary>
    AppearanceState State { get; }

    /// <summary>
    /// The accent choices, hex colors for the dark palette
    /// </summary>
    IReadOnlyList<string> AccentColors { get; }

    /// <summary>
    /// Raised after a new look is applied
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Applies the saved look, called once at startup
    /// </summary>
    void Initialize();

    /// <summary>
    /// Applies and saves a new look
    /// </summary>
    void Apply(AppearanceState state);
}
