using System.Threading;
using System.Threading.Tasks;

namespace VNO.Server.Services;

/// <summary>
/// Persists the server settings back to the legacy data files
/// </summary>
/// <remarks>
/// The loader reads init.ini, areas.ini, musiclist.txt, and charlist.txt, this
/// store writes the same files so an edit made in the console survives a restart
/// and stays editable by hand, the way the legacy editors saved
/// </remarks>
public interface IServerSettingsStore
{
    /// <summary>
    /// Writes the given settings to the data folder
    /// </summary>
    Task SaveAsync(ServerSettings settings, CancellationToken cancellationToken = default);
}
