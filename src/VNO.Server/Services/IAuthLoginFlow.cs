using System.Threading;
using System.Threading.Tasks;

namespace VNO.Server.Services;

/// <summary>
/// The interactive auth server sign in shared by the shell startup and the
/// dashboard connect button
/// </summary>
public interface IAuthLoginFlow
{
    /// <summary>
    /// Signs in with the saved account when one is remembered, otherwise keeps
    /// asking through a blocking modal until the auth server accepts. Returns
    /// false only when nothing can render the modal
    /// </summary>
    Task<bool> SignInAsync(CancellationToken cancellationToken = default);
}
