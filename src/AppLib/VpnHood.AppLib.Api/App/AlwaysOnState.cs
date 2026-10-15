using VpnHood.Net.Toolkit.ApiClients;

namespace VpnHood.AppLib.Api.App;

// What always on is doing where the app keeps it itself (AlwaysOnSupport.App), for the window and
// `status`: why it waits and until when, or why it has stopped.
public class AlwaysOnState
{
    public required AlwaysOnStatus Status { get; init; }

    // when it connects again, while it waits
    public required DateTime? RetryTime { get; init; }

    // the failure it waits after, or the one that stopped it; null when a person's disconnect stopped it
    public required ApiError? Error { get; init; }
}
