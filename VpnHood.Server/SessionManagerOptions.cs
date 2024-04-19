namespace VpnHood.Server;

internal class SessionManagerOptions
{
    public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromMinutes(1);
}