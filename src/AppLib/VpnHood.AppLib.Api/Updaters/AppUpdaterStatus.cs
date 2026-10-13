namespace VpnHood.AppLib.Api.Updaters;

public class AppUpdaterStatus
{
    public required DateTime? CheckedTime { get; init; }
    public required VersionStatus VersionStatus { get; init; }
    public required PublishInfo? PublishInfo { get; init; }
    public required bool Prompt { get; init; }

    // The update the notice offers, where the app updates itself: null where it does not (a store's own
    // screen shows it, or this install cannot take the package), and the notice keeps its link.
    public required AppUpdateState? UpdateState { get; init; }
}