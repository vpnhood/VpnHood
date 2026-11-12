using VpnHood.AppLib.App;
using VpnHood.AppUi.Hosting.Desktop.Abstractions;

namespace VpnHood.AppUi.Hosting.Desktop;

/// <summary>
/// Represents what a desktop head says for itself: the init params of every platform, and the answers the commands and
/// the window need.
/// </summary>
/// <remarks>
/// Where the machine keeps things and what the service is are the platform's answers; the service's host lays its
/// storage out itself, so a desktop head names no storage folder. The options factory runs only where the app runs.
/// </remarks>
public class DesktopInitParams : AppInitParams
{
    /// <summary>
    /// Gets the head's UI, which the window runs on the host's main thread against the running app: the API over loopback,
    /// and the UI's own content extracted under the current user's cache.
    /// </summary>
    public required IDesktopUi Ui { get; init; }

    //

    /// <summary>
    /// Gets a value indicating whether the head offers adding access keys: the same answer the head gives the app's
    /// options, which must agree.
    /// </summary>
    /// <remarks>
    /// Where it is <see langword="false"/>, the commands that name, list and add profiles, and the <c>--profile</c> option,
    /// are not printed, parsed or offered in help. It is required, as it is the one answer that differs between the two
    /// shipped heads.
    /// </remarks>
    public required bool IsAddAccessKeySupported { get; init; }

    /// <summary>
    /// Gets the extensions of an access key's file, as one sent to the person, that open in this app, which imports the
    /// key. The default is none.
    /// </summary>
    /// <remarks>On Windows, <c>service install</c> registers them in an installer's layout.</remarks>
    public IReadOnlyList<string> AccessKeyFileExtensions { get; init; } = [];

    /// <summary>Gets the schemes of a link that is an access key itself, as <c>vh:</c>, which opens in this app the same way. The default is none.</summary>
    public IReadOnlyList<string> AccessKeySchemes { get; init; } = [];

    /// <summary>
    /// Gets where this package placed the UI's content, beside the binary. The default is <c>assets/ui.zip</c>, where the
    /// Classic assets place it for every head that imports them.
    /// </summary>
    internal string UiZipAssetPath { get; init; } = "assets/ui.zip";
}
