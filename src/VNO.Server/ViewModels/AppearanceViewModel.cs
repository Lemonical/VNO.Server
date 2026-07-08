using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNO.Server.Services;
using VNO.Server.Theming;

namespace VNO.Server.ViewModels;

/// <summary>
/// View model for the appearance page, theme, accent, density, and presets
/// </summary>
public sealed partial class AppearanceViewModel : ViewModelBase
{
    private readonly IThemeManager _themes;
    private readonly IConsoleInteraction _interaction;

    [ObservableProperty]
    private bool _isDark = true;

    [ObservableProperty]
    private bool _isCompact;

    /// <summary>
    /// Creates the appearance page over the theme manager
    /// </summary>
    public AppearanceViewModel(IThemeManager themes, IConsoleInteraction interaction)
    {
        _themes = themes;
        _interaction = interaction;
        for (var i = 0; i < _themes.AccentColors.Count; i++)
        {
            Swatches.Add(new AccentSwatchViewModel(i, _themes.AccentColors[i], SelectAccent));
        }
        _themes.Changed += (_, _) => SyncFromState();
        SyncFromState();
    }

    /// <summary>
    /// The accent choices
    /// </summary>
    public ObservableCollection<AccentSwatchViewModel> Swatches { get; } = new();

    [RelayCommand]
    private void SetDark() => Apply(_themes.State with { Theme = ConsoleThemeVariant.Dark });

    [RelayCommand]
    private void SetLight() => Apply(_themes.State with { Theme = ConsoleThemeVariant.Light });

    [RelayCommand]
    private void SetComfortable() => Apply(_themes.State with { Density = ConsoleDensity.Comfortable });

    [RelayCommand]
    private void SetCompact() => Apply(_themes.State with { Density = ConsoleDensity.Compact });

    [RelayCommand]
    private void ApplyPreset(string preset)
    {
        var state = preset switch
        {
            "ocean" => new AppearanceState(ConsoleThemeVariant.Dark, 4, ConsoleDensity.Comfortable),
            "rose" => new AppearanceState(ConsoleThemeVariant.Dark, 3, ConsoleDensity.Comfortable),
            "daylight" => new AppearanceState(ConsoleThemeVariant.Light, 1, ConsoleDensity.Comfortable),
            _ => AppearanceState.Default,
        };
        _themes.Apply(state);
        _interaction.ShowToast("Preset applied", ToastSeverity.Success);
    }

    private void SelectAccent(int index)
    {
        Apply(_themes.State with { AccentIndex = index });
        _interaction.ShowToast("Accent color updated", ToastSeverity.Success);
    }

    private void Apply(AppearanceState state) => _themes.Apply(state);

    private void SyncFromState()
    {
        var state = _themes.State;
        IsDark = state.Theme == ConsoleThemeVariant.Dark;
        IsCompact = state.Density == ConsoleDensity.Compact;
        foreach (var swatch in Swatches)
        {
            swatch.IsSelected = swatch.Index == state.AccentIndex;
        }
    }
}
