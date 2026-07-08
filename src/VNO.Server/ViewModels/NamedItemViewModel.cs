using System;
using CommunityToolkit.Mvvm.Input;

namespace VNO.Server.ViewModels;

/// <summary>
/// One removable name in a configuration list, an area, a track, or a character
/// </summary>
public sealed partial class NamedItemViewModel
{
    private readonly Action<string> _remove;

    /// <summary>
    /// Creates the row with its remove action
    /// </summary>
    public NamedItemViewModel(string name, Action<string> remove)
    {
        Name = name;
        _remove = remove;
    }

    /// <summary>
    /// The list entry
    /// </summary>
    public string Name { get; }

    [RelayCommand]
    private void Remove() => _remove(Name);
}
