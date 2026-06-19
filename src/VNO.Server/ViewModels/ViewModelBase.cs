using CommunityToolkit.Mvvm.ComponentModel;

namespace VNO.Server.ViewModels;

/// <summary>
/// Base class for every view model in the server app
/// </summary>
/// <remarks>
/// Inherits the property change support from the Community Toolkit so derived
/// types can use the ObservableProperty and RelayCommand source generators
/// </remarks>
public abstract class ViewModelBase : ObservableObject
{
}
