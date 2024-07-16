namespace VpnHood.Client.App;

internal class VersionCheckResult
{
    public required DateTime CheckedTime { get; init; }
    public required Version LocalVersion { get; init; }
    public required VersionStatus VersionStatus { get; init; }
    public required PublishInfo PublishInfo { get; init; }
}