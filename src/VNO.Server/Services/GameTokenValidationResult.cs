namespace VNO.Server.Services;

/// <summary>
/// Result of asking Master to redeem a client game-handoff credential
/// </summary>
/// <param name="IsValid">Whether the credential was accepted and consumed</param>
/// <param name="Username">Canonical Master account name on success</param>
public sealed record GameTokenValidationResult(bool IsValid, string Username)
{
    /// <summary>
    /// Uniform failed validation result
    /// </summary>
    public static GameTokenValidationResult Invalid { get; } = new(false, string.Empty);
}
