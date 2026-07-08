using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Optris.Icons.Avalonia;
using Optris.Icons.Avalonia.FontAwesome;
using VNO.Server.ViewModels;
using VNO.Server.Views;

namespace VNO.Server;

/// <summary>
/// The Avalonia application for the server admin tool
/// </summary>
/// <remarks>
/// Resolves the main window and its view model from the dependency injection
/// container that <see cref="Program"/> builds, so nothing is constructed by hand
/// </remarks>
public sealed class App : Application
{
    private readonly IServiceProvider _services;

    // the icon font must be registered before any view asks for an fa key, and
    // exactly once per process even when headless tests boot several sessions
    static App() => IconProvider.Current.Register<FontAwesomeIconProvider>();

    /// <summary>
    /// Required by the Avalonia designer, builds an empty service set
    /// </summary>
    public App() => _services = new ServiceCollection().BuildServiceProvider();

    /// <summary>
    /// Creates the app with the real service provider
    /// </summary>
    public App(IServiceProvider services) => _services = services;

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // apply the saved palette before the window exists so the first frame
            // already wears the chosen theme
            _services.GetService<Theming.IThemeManager>()?.Initialize();
            var viewModel = _services.GetService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // hosting requires an auth server account, run the blocking sign in
            // as soon as the window is up
            if (viewModel is not null)
            {
                Dispatcher.UIThread.Post(() => _ = viewModel.StartupAsync());
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
