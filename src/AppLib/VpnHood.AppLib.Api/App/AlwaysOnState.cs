using VpnHood.Net.Toolkit.ApiClients;

namespace VpnHood.AppLib.Api.App;

/// <summary>
/// Represents what always-on does where the app keeps it itself (<see cref="AlwaysOnSupport.App"/>): why it waits and
/// until when, or why it has stopped.
/// </summary>
public class AlwaysOnState
{
    /// <summary>Gets where always-on stands.</summary>
    public required AlwaysOnStatus Status { get; init; }

    /// <summary>Gets when it connects again, while it waits, or <see langword="null"/>.</summary>
    public required DateTime? RetryTime { get; init; }

    /// <summary>Gets the failure it waits after, or the one that stopped it; <see langword="null"/> where a person's disconnect stopped it.</summary>
    public required ApiError? Error { get; init; }
}
