namespace VpnHood.Server;

internal class SessionManagerOptions
{
    public required TimeSpan CleanupInterval { get; init; }
}