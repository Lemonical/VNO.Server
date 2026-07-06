using System;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VNO.Core.Networking;
using VNO.Server.Admin;
using VNO.Server.Services;
using VNO.Server.Theming;
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
        var services = new ServiceCollection();

        // the issue log rides the logging pipeline so any warning or error any
        // service emits lands on the console dashboard
        var issueLog = new IssueLog();
        services.AddSingleton<IIssueLog>(issueLog);

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddConsole();
            builder.AddProvider(issueLog);
        });

        // settings come from the legacy server data files, not a json config, so the
        // same init.ini, areas.ini, and lists an operator edits drive the port
        services.AddSingleton<IOptions<ServerSettings>>(Options.Create(ServerSettingsLoader.Load()));

        // networking, the server hosts players and the client reaches the AS. Each hop picks
        // its transport from settings so a self hoster can host players over ws while dialing
        // the AS over wss, both speaking the same message contract
        services.AddSingleton<IMessageServer>(BuildListener);
        services.AddSingleton<IMessageClient>(BuildAuthLink);

        // application services
        services.AddSingleton<IUserRegistry, UserRegistry>();
        services.AddSingleton<IBanRegistry, BanRegistry>();
        services.AddSingleton<IGameHost, GameHost>();
        services.AddSingleton<IModerationService, ModerationService>();
        services.AddSingleton<IAuthServerLink, AuthServerLink>();
        services.AddSingleton<IServerSettingsStore, ServerSettingsStore>();

        // the admin controller is the one surface every console frontend drives,
        // the Avalonia window here and a command line client later
        services.AddSingleton<IServerAdminController, ServerAdminController>();

        // console presentation services
        services.AddSingleton<IAppearanceStore, AppearanceStore>();
        services.AddSingleton<IThemeManager, ThemeManager>();
        services.AddSingleton<IConsoleInteraction, ConsoleInteraction>();
        services.AddSingleton<IAuthLoginFlow, AuthLoginFlow>();

        // view models
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }

    // the player facing listener, game payloads carry evidence and animation so it takes the
    // larger inbound cap. On WebSocket the ban registry refuses banned peers at the handshake
    private static IMessageServer BuildListener(IServiceProvider services)
    {
        var settings = services.GetRequiredService<IOptions<ServerSettings>>().Value;
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();

        if (settings.ListenTransport != Transport.WebSocket)
        {
            return MessageTransportFactory.CreateServer(Transport.Tcp, loggerFactory);
        }

        var options = new WebSocketTransportOptions
        {
            MaxInboundBytes = VNO.Core.Protocol.ProtocolConstants.MaxGameMessageBytes,
        };
        var server = new WebSocketMessageServer(loggerFactory, options);
        var bans = services.GetRequiredService<IBanRegistry>();
        server.IsAddressBanned = (address, _) => new System.Threading.Tasks.ValueTask<bool>(bans.IsAddressBanned(address));
        return server;
    }

    // the outgoing auth server link, small frames, TLS when dialing managed ingress
    private static IMessageClient BuildAuthLink(IServiceProvider services)
    {
        var settings = services.GetRequiredService<IOptions<ServerSettings>>().Value;
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();

        var options = new WebSocketTransportOptions
        {
            UseTls = settings.AuthUseTls,
            MaxInboundBytes = VNO.Core.Protocol.ProtocolConstants.MaxAuthMessageBytes,
        };
        return MessageTransportFactory.CreateClient(settings.AuthTransport, loggerFactory, options);
    }
}
