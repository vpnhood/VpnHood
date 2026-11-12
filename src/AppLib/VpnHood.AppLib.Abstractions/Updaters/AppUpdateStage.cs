namespace VpnHood.AppLib.Abstractions.Updaters;

/// <summary>Specifies where an update of the app stands.</summary>
public enum AppUpdateStage
{
    /// <summary>There is no update, or the channel does not update this copy.</summary>
    None,

    /// <summary>An update is there to start.</summary>
    Available,

    /// <summary>The update is downloading.</summary>
    Downloading,

    /// <summary>
    /// The update is downloaded, and waits for its install: the person's Install, or the channel's automatic update where
    /// it is on.
    /// </summary>
    Downloaded,

    /// <summary>The update is installing.</summary>
    Installing,

    /// <summary>The last download or install failed.</summary>
    Failed
}
