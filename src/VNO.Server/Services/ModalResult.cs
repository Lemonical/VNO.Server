using System;

namespace VNO.Server.Services;

/// <summary>
/// What the operator entered before confirming a modal
/// </summary>
/// <remarks>
/// A null duration means permanent. A cancelled modal yields no result at all.
/// The account fields are only filled by a credentials modal
/// </remarks>
public sealed record ModalResult(
    string Reason,
    TimeSpan? Duration,
    string Message,
    string Username = "",
    string Password = "",
    bool Remember = false);
