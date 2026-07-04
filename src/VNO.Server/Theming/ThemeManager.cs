using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using VNO.Server.Services;

namespace VNO.Server.Theming;

/// <summary>
/// Default theme manager over the Avalonia application resources
/// </summary>
/// <remarks>
/// Every view binds the console palette with DynamicResource, so replacing the
/// resource values here restyles the open window without rebuilding any view.
/// The palettes mirror the console design, flat surfaces with hairline borders,
/// no gradients or glows
/// </remarks>
public sealed class ThemeManager : IThemeManager
{
    private readonly IAppearanceStore _store;

    // accent choices, the first entry is the theme's own gold
    private static readonly string[] DarkAccents =
        { "#c9953c", "#6366f1", "#10b981", "#f43f5e", "#06b6d4", "#a855f7" };
    private static readonly string[] LightAccents =
        { "#9e7220", "#4f46e5", "#0d9463", "#dc2650", "#0891b2", "#9333ea" };

    /// <summary>
    /// Creates the manager with its persistence
    /// </summary>
    public ThemeManager(IAppearanceStore store)
    {
        _store = store;
        State = AppearanceState.Default;
    }

    /// <inheritdoc />
    public AppearanceState State { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<string> AccentColors => DarkAccents;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void Initialize() => ApplyCore(_store.Load(), save: false);

    /// <inheritdoc />
    public void Apply(AppearanceState state) => ApplyCore(state, save: true);

    private void ApplyCore(AppearanceState state, bool save)
    {
        State = state;
        var app = Application.Current;
        if (app is not null)
        {
            var dark = state.Theme == ConsoleThemeVariant.Dark;
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

            var r = app.Resources;
            if (dark)
            {
                SetBrush(r, "CBg0", "#0d0f14");
                SetBrush(r, "CBg1", "#13161f");
                SetBrush(r, "CBg2", "#181b26");
                SetBrush(r, "CBg3", "#1e2130");
                SetBrush(r, "CBg4", "#252839");
                SetBrush(r, "CBg5", "#2c3044");
                SetBrush(r, "CBorder", "#0FFFFFFF");
                SetBrush(r, "CBorderStrong", "#1FFFFFFF");
                SetBrush(r, "CText0", "#e0e2ec");
                SetBrush(r, "CText1", "#9499ae");
                SetBrush(r, "CText2", "#5c6178");
                SetBrush(r, "CText3", "#3e4358");
                SetBrush(r, "CGreen", "#4ade80");
                SetBrush(r, "CGreenMuted", "#1A4ade80");
                SetBrush(r, "CRed", "#f87171");
                SetBrush(r, "CRedMuted", "#1Af87171");
                SetBrush(r, "CBlue", "#818cf8");
                SetBrush(r, "CBlueMuted", "#1A818cf8");
                SetBrush(r, "CAmber", "#fbbf24");
                SetBrush(r, "CAccentFg", "#0d0f14");
                SetBrush(r, "CScrim", "#8C000000");
            }
            else
            {
                SetBrush(r, "CBg0", "#f2f3f6");
                SetBrush(r, "CBg1", "#e8e9ee");
                SetBrush(r, "CBg2", "#edeef2");
                SetBrush(r, "CBg3", "#ffffff");
                SetBrush(r, "CBg4", "#f0f1f5");
                SetBrush(r, "CBg5", "#e2e3e9");
                SetBrush(r, "CBorder", "#12000000");
                SetBrush(r, "CBorderStrong", "#21000000");
                SetBrush(r, "CText0", "#13151f");
                SetBrush(r, "CText1", "#555a6e");
                SetBrush(r, "CText2", "#8c91a5");
                SetBrush(r, "CText3", "#b8bbc8");
                SetBrush(r, "CGreen", "#16a34a");
                SetBrush(r, "CGreenMuted", "#1416a34a");
                SetBrush(r, "CRed", "#dc2626");
                SetBrush(r, "CRedMuted", "#14dc2626");
                SetBrush(r, "CBlue", "#4f46e5");
                SetBrush(r, "CBlueMuted", "#144f46e5");
                SetBrush(r, "CAmber", "#d97706");
                SetBrush(r, "CAccentFg", "#ffffff");
                SetBrush(r, "CScrim", "#59000000");
            }

            var accents = dark ? DarkAccents : LightAccents;
            var index = state.AccentIndex >= 0 && state.AccentIndex < accents.Length
                ? state.AccentIndex
                : 0;
            var accent = Color.Parse(accents[index]);
            r["CAccent"] = new SolidColorBrush(accent);
            r["CAccentHover"] = new SolidColorBrush(Lighten(accent));
            r["CAccentMuted"] = new SolidColorBrush(accent, dark ? 0.14 : 0.10);

            var compact = state.Density == ConsoleDensity.Compact;
            r["CFontBase"] = compact ? 12.0 : 13.0;
            r["CPagePad"] = compact ? new Thickness(16) : new Thickness(24);
        }

        if (save)
        {
            _store.Save(state);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void SetBrush(IResourceDictionary resources, string key, string hex) =>
        resources[key] = new SolidColorBrush(Color.Parse(hex));

    private static Color Lighten(Color color) =>
        Color.FromArgb(
            color.A,
            (byte)Math.Min(255, color.R + 16),
            (byte)Math.Min(255, color.G + 16),
            (byte)Math.Min(255, color.B + 16));
}
