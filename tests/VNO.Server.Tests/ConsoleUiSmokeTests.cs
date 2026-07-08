using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VNO.Core.Models;
using VNO.Server;
using VNO.Server.Admin;
using VNO.Server.Services;
using VNO.Server.Theming;
using VNO.Server.ViewModels;
using VNO.Server.Views;
using Xunit;

namespace VNO.Server.Tests;

/// <summary>
/// Renders the console window headlessly and checks every section draws
/// </summary>
/// <remarks>
/// Uses the real views, view models, theme manager, and admin controller over the
/// test doubles, so a broken binding or style shows up as a failed capture. Set
/// VNO_UI_SHOTS to a directory to also write the frames out as png files
/// </remarks>
public sealed class ConsoleUiSmokeTests
{
    /// <summary>
    /// Builds the headless application the session boots
    /// </summary>
    public static class TestAppBuilder
    {
        /// <summary>
        /// Configures the real app class over the headless platform with skia
        /// </summary>
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UseSkia()
                .WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    [Fact]
    public async Task Every_console_section_renders_a_frame()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        // return a value so the Func<Task<T>> overload is picked and the whole
        // async body is awaited, the Func<T> overload would only run the sync part
        await session.Dispatch<object?>(async () =>
        {
            var (shell, admin, host, users, _) = BuildShell();

            // seed a believable world so the captures show real rows
            var zephyr = users.Add("s1", "192.168.1.42");
            zephyr.Name = "Zephyr";
            zephyr.Character = "Phoenix Wright";
            var cascade = users.Add("s2", "10.0.0.15");
            cascade.Name = "Cascade";
            cascade.Character = "Miles Edgeworth";
            cascade.IsModerator = true;
            var noctis = users.Add("s3", "172.16.0.8");
            noctis.Name = "Noctis";
            noctis.Character = "Apollo Justice";
            noctis.IsMuted = true;
            host.PlayerCount = 3;
            host.RaiseUsersChanged();
            host.RaiseLog("Player 1 connected from 192.168.1.42");
            host.RaiseLog("Moderator 2 muted player 3");
            host.RaiseLog("Failed to relay scene effect: invalid format");
            host.RaiseOoc(new OocLine("Zephyr", "hey everyone, ready to start?"));
            host.RaiseOoc(new OocLine("Server", "Welcome to the server! Please review the rules."));
            host.RaiseIc(new IcLine("Phoenix Wright", "Zephyr", "Your Honor, the defense is ready."));

            var window = new MainWindow { DataContext = shell };
            window.Show();

            var shots = Environment.GetEnvironmentVariable("VNO_UI_SHOTS");
            foreach (var section in Enum.GetValues<ConsoleSection>())
            {
                shell.CurrentSection = section;
                if (section == ConsoleSection.Players)
                {
                    shell.Players.SelectCommand.Execute(shell.Players.Players[0]);
                }
                await Task.Yield();
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                if (!string.IsNullOrEmpty(shots))
                {
                    Directory.CreateDirectory(shots);
                    frame.Save(Path.Combine(shots, $"{section}.png".ToLowerInvariant()));
                }
            }

            window.Close();
            return null;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task The_light_theme_and_broadcast_modal_render()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch<object?>(async () =>
        {
            var (shell, _, _, _, _) = BuildShell();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            // flip to light and open the broadcast modal, then draw one frame
            shell.Appearance.SetLightCommand.Execute(null);
            var send = shell.SendNoticeCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
            Assert.NotNull(shell.ActiveModal);

            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var shots = Environment.GetEnvironmentVariable("VNO_UI_SHOTS");
            if (!string.IsNullOrEmpty(shots))
            {
                Directory.CreateDirectory(shots);
                var path = Path.Combine(shots, "light-modal.png");
                frame.Save(path);
                Assert.True(File.Exists(path), $"expected {path}");
            }

            shell.ActiveModal!.CancelCommand.Execute(null);
            await send;
            window.Close();
            return null;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task The_blocking_sign_in_modal_renders_and_ignores_cancel()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch<object?>(async () =>
        {
            var (shell, _, _, _, interaction) = BuildShell();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            var pending = interaction.ShowModalAsync(new ModalRequest(
                "Auth Server Sign In",
                "Hosting a server requires a VNO account.",
                "Sign In",
                IsDestructive: false,
                ShowCredentials: true,
                ShowRemember: true,
                AllowCancel: false,
                InitialUsername: "operator",
                InitialRemember: true));
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
            Assert.NotNull(shell.ActiveModal);

            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var shots = Environment.GetEnvironmentVariable("VNO_UI_SHOTS");
            if (!string.IsNullOrEmpty(shots))
            {
                Directory.CreateDirectory(shots);
                frame.Save(Path.Combine(shots, "sign-in-modal.png"));
            }

            // a blocking modal cannot be cancelled and cannot be confirmed empty
            shell.ActiveModal!.CancelCommand.Execute(null);
            Assert.NotNull(shell.ActiveModal);
            shell.ActiveModal.Password = string.Empty;
            shell.ActiveModal.ConfirmCommand.Execute(null);
            Assert.NotNull(shell.ActiveModal);

            // filled credentials confirm and flow back to the caller
            shell.ActiveModal.Password = "hunter2";
            shell.ActiveModal.ConfirmCommand.Execute(null);
            var result = await pending;
            Assert.Null(shell.ActiveModal);
            Assert.Equal("operator", result!.Username);
            Assert.Equal("hunter2", result.Password);
            Assert.True(result.Remember);

            window.Close();
            return null;
        }, CancellationToken.None);
    }

    private static (MainWindowViewModel Shell, ServerAdminController Admin, FakeGameHost Host,
        UserRegistry Users, ConsoleInteraction Interaction)
        BuildShell()
    {
        var host = new FakeGameHost();
        var users = new UserRegistry();
        var bans = new BanRegistry();
        bans.Add(new BanEntry
        {
            Kind = BanKind.Account,
            Target = "TrollMaster",
            Reason = "Spamming OOC chat",
            PlacedBy = "admin",
        });
        bans.Add(new BanEntry
        {
            Kind = BanKind.IpAddress,
            Target = "45.33.22.11",
            Reason = "Repeated harassment",
            PlacedBy = "Cascade",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        });
        var issues = new IssueLog();
        var settings = new ServerSettings { Name = "Turnabout Central" };
        var admin = new ServerAdminController(
            host, new FakeModeration(), new FakeAuthLink(), users, bans, issues,
            new FakeSettingsStore(), Options.Create(settings));

        // issue rows come in through the logging pipeline like in the real app
        var logger = ((ILoggerProvider)issues).CreateLogger("VNO.Server.Services.GameHost");
        logger.LogWarning("Heartbeat response delayed (>2s)");
        logger.LogError("Failed to load musiclist.txt entry");

        var tempDir = Path.Combine(Path.GetTempPath(), "vno-ui-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeManager(new AppearanceStore(tempDir));
        themes.Initialize();
        var interaction = new ConsoleInteraction();

        var authFlow = new AuthLoginFlow(admin, interaction);
        var shell = new MainWindowViewModel(
            admin,
            interaction,
            themes,
            authFlow,
            new DashboardViewModel(admin, interaction, authFlow),
            new PlayersViewModel(admin, interaction),
            new ChatViewModel(admin),
            new ConfigurationViewModel(admin, interaction),
            new BansViewModel(admin, interaction),
            new AppearanceViewModel(themes, interaction));
        return (shell, admin, host, users, interaction);
    }
}
