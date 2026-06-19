using System;
using Avalonia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VNO.Core.Networking;
using VNO.Server.Services;
using VNO.Server.ViewModels;

namespace VNO.Server;

/// <summary>
/// Entry point and composition root for the server app
/// </summary>
/// <remarks>
/// Builds the dependency injection container then starts Avalonia. Every service
/// and view model is registered here so the rest of the code only asks for
/// interfaces
/// </remarks>
public static class Program
{
    /// <summary>
    /// Process entry point
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        var services = BuildServiceProvider();
        BuildAvaloniaApp(services).StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Builds the Avalonia app and hands it the service provider
    /// </summary>
    public static AppBuilder BuildAvaloniaApp(IServiceProvider services) =>
        AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Designer entry point, builds the app without a configured provider
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static IServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddConsole();
        });

        services.Configure<ServerSettings>(configuration.GetSection("Server"));

        // networking, the server hosts players and the client reaches the AS
        services.AddSingleton<IMessageServer, TcpMessageServer>();
        services.AddSingleton<IMessageClient, TcpMessageClient>();

        // application services
        services.AddSingleton<IUserRegistry, UserRegistry>();
        services.AddSingleton<IBanRegistry, BanRegistry>();
        services.AddSingleton<IGameHost, GameHost>();
        services.AddSingleton<IModerationService, ModerationService>();
        services.AddSingleton<IAuthServerLink, AuthServerLink>();

        // view models
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
