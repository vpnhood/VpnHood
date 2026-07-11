namespace VpnHood.AppLib.Services.Updaters;

public class AppUpdaterStatus
{
    public required DateTime? CheckedTime { get; init; }
    public required VersionStatus VersionStatus { get; init; }
    public required PublishInfo? PublishInfo { get; init; }
    public required bool Prompt { get; init; }
}