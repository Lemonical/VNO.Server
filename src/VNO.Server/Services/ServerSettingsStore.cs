using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VNO.Core.Networking;

namespace VNO.Server.Services;

/// <summary>
/// Default settings store that writes the legacy data files
/// </summary>
/// <remarks>
/// Emits the exact shapes <see cref="ServerSettingsLoader"/> reads back, init.ini
/// with the Server and AS sections, areas.ini with one section per area, and the
/// one item per line music and character lists
/// </remarks>
public sealed class ServerSettingsStore : IServerSettingsStore
{
    private readonly string _dataDirectory;

    /// <summary>
    /// Creates a store rooted at the data folder next to the executable
    /// </summary>
    public ServerSettingsStore() : this(System.AppContext.BaseDirectory)
    {
    }

    /// <summary>
    /// Creates a store rooted at the data folder under the given base directory
    /// </summary>
    public ServerSettingsStore(string baseDirectory) =>
        _dataDirectory = Path.Combine(baseDirectory, "data");

    /// <inheritdoc />
    public async Task SaveAsync(ServerSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_dataDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(_dataDirectory, "init.ini"), BuildInit(settings), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(_dataDirectory, "areas.ini"), BuildAreas(settings), cancellationToken).ConfigureAwait(false);
        await File.WriteAllLinesAsync(
            Path.Combine(_dataDirectory, "musiclist.txt"), settings.Music, cancellationToken).ConfigureAwait(false);
        await File.WriteAllLinesAsync(
            Path.Combine(_dataDirectory, "charlist.txt"), settings.Characters, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildInit(ServerSettings settings)
    {
        var invariant = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.AppendLine("[Server]");
        text.AppendLine(invariant, $"name={settings.Name}");
        text.AppendLine(invariant, $"port={settings.ListenPort}");
        text.AppendLine(invariant, $"transport={TransportWord(settings.ListenTransport)}");
        text.AppendLine(invariant, $"public={(settings.IsPublic ? "1" : "0")}");
        text.AppendLine(invariant, $"heartbeat={settings.HeartbeatSeconds}");
        text.AppendLine(invariant, $"moderatorpassword={settings.ModeratorPassword}");
        text.AppendLine(invariant, $"chatburst={settings.ChatBurst}");
        text.AppendLine(invariant, $"chatrate={settings.ChatMessagesPerSecond}");
        text.AppendLine();
        text.AppendLine("[AS]");
        text.AppendLine(invariant, $"host={settings.AuthServerHost}");
        text.AppendLine(invariant, $"port={settings.AuthServerPort}");
        text.AppendLine(invariant, $"transport={TransportWord(settings.AuthTransport)}");
        text.AppendLine(invariant, $"tls={(settings.AuthUseTls ? "1" : "0")}");
        text.AppendLine(invariant, $"username={settings.AuthUsername}");
        text.AppendLine(invariant, $"password={settings.AuthPassword}");
        text.AppendLine(invariant, $"remember={(settings.AuthRemember ? "1" : "0")}");
        return text.ToString();
    }

    private static string BuildAreas(ServerSettings settings)
    {
        var text = new StringBuilder();
        foreach (var area in settings.Areas)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"[{area}]");
        }
        return text.ToString();
    }

    private static string TransportWord(Transport transport) =>
        transport == Transport.WebSocket ? "websocket" : "tcp";
}
