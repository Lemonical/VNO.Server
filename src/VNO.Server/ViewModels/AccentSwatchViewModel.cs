using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VNO.Server.ViewModels;

/// <summary>
/// One selectable accent color square
/// </summary>
public sealed partial class AccentSwatchViewModel : ViewModelBase
{
    private readonly Action<int> _select;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Creates the swatch with its color and select action
    /// </summary>
    public AccentSwatchViewModel(int index, string hex, Action<int> select)
    {
        Index = index;
        Brush = new SolidColorBrush(Color.Parse(hex));
        _select = select;
    }

    /// <summary>
    /// Position in the accent list
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// The color itself
    /// </summary>
    public IBrush Brush { get; }

    [RelayCommand]
    private void Select() => _select(Index);
}
