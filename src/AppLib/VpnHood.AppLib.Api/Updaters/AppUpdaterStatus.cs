namespace VpnHood.AppLib.Api.Updaters;

/// <summary>Represents where the app's update stands, as the notice reads it.</summary>
public class AppUpdaterStatus
{
    /// <summary>Gets when the app last recorded a check, of the store or of the feed, in UTC, or <see langword="null"/>.</summary>
    public required DateTime? CheckedTime { get; init; }

    /// <summary>Gets how this version stands: against the store's offer where one is made, else against the feed's release.</summary>
    public required VersionStatus VersionStatus { get; init; }

    /// <summary>Gets the feed's release, or <see langword="null"/> where none was read.</summary>
    public required PublishInfo? PublishInfo { get; init; }

    /// <summary>
    /// Gets a value indicating whether the notice should show: always for an update downloading, installing or failed; and,
    /// unless a desktop's automatic update still has time to install the release itself, for a downloaded update, and for
    /// an old version the store does not offer to update, outside the person's Later.
    /// </summary>
    public required bool Prompt { get; init; }

    /// <summary>
    /// Gets the update the notice offers where the app updates itself, or <see langword="null"/> where it does not, as where
    /// a store's own screen shows it, and the notice keeps its link.
    /// </summary>
    public required AppUpdateState? UpdateState { get; init; }
}