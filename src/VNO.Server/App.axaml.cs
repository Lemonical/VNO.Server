using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
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
            var viewModel = _services.GetService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
