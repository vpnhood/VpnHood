namespace VpnHood.AppLib.Api.Updaters;

// The update the notice offers where the app updates itself: the feed's newer version, and how far
// its update has come

/// <summary>Represents an update of the app as it stands.</summary>
public class AppUpdateState
{
    /// <summary>Gets the version the update brings.</summary>
    public required Version Version { get; init; }

    /// <summary>Gets where the update stands.</summary>
    public required AppUpdateStage Stage { get; init; }

    /// <summary>Gets the bytes of the update downloaded so far, as the channel tells them, or 0 where it tells none.</summary>
    public required long DownloadedBytes { get; init; }

    // null until the server has told the size

    /// <summary>Gets the update's size, in bytes, as the channel tells it, or <see langword="null"/> where it tells none, as before the server has told it.</summary>
    public required long? TotalBytes { get; init; }

    // why the last download or install failed, with AppUpdateStage.Failed

    /// <summary>Gets why the update failed, in its download, its install or a read of its state, with <see cref="AppUpdateStage.Failed"/>, or <see langword="null"/>.</summary>
    public required string? ErrorMessage { get; init; }

    // with AppUpdateStage.Downloaded: the system still has to let the app install apps, which Install asks
    // first, so the person's next Install installs

    /// <summary>
    /// Gets a value indicating whether, with <see cref="AppUpdateStage.Downloaded"/>, the system still has to let the
    /// app install apps, which Install asks first, as Android's "Install unknown apps", so the person's next Install
    /// installs.
    /// </summary>
    public bool IsInstallPermissionNeeded { get; init; }
}
