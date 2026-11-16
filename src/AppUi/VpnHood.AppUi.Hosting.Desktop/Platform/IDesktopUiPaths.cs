namespace VpnHood.AppUi.Hosting.Desktop.Platform;

/// <summary>
/// Defines where the window keeps things: the window's share of an install's paths, and all a host without a service
/// hands it.
/// </summary>
public interface IDesktopUiPaths
{
    /// <summary>
    /// Gets the app's id, which no other app on the machine has and a Debug build does not share with a release: it names
    /// the person's folders and the window's single instance.
    /// </summary>
    string AppId { get; }

    /// <summary>Gets the install's own name, as the platform spells it, which names the service.</summary>
    string InstanceName { get; }

    /// <summary>
    /// Gets the UI's own folder, per person, as the service's storage is not theirs to write: the UI's extracted content,
    /// and a web view's profile.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The platform cannot name the folder, as the Windows plug-in's outside its package.
    /// </exception>
    string UiDataPath { get; }

    /// <summary>
    /// Gets the folder of the UI's own extracted content, under the UI's folder, named apart from the app's own copies,
    /// which may share that folder where the app runs in the window's process.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The platform cannot name the folder, as the Windows plug-in's outside its package.
    /// </exception>
    string UiContentCachePath => Path.Combine(UiDataPath, "assets", "window");
}
