using VpnHood.AppLib.Api;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppLib.App.WebHosting;

/// <summary>
/// Provides what a web host is made with (<see cref="IAppWebHostFactory"/>): the API it puts on HTTP, the page and the
/// UI's files it serves, the port, and how it behaves. The app makes one for each host, as the two are not told the
/// same.
/// </summary>
/// <remarks>
/// The host does as it is told rather than infer, so the server half names nothing of the engine and builds against
/// the API alone.
/// </remarks>
public class WebHostCreateParams
{
    /// <summary>
    /// Gets the app's API, the same instance for every transport, so a paired browser and the device's own UI agree.
    /// </summary>
    public required VpnHoodApi Api { get; init; }

    /// <summary>
    /// Gets the page the host serves, <c>index.html</c> at its root, to this device and to a paired one alike: the UI's
    /// browser build, as the head shipped it (<see cref="AppOptions.WebRootZipAsset"/>), the same instance for both
    /// hosts.
    /// </summary>
    public required IAssetProvider WebRoot { get; init; }

    /// <summary>
    /// Gets the files the app's UI draws from, which the host serves at <c>/assets/</c> to a paired phone's page, the
    /// instance the app's own UI reads (<see cref="VpnHoodApp.UiAssetProvider"/>), or <see langword="null"/> for a UI
    /// that brings none of its own.
    /// </summary>
    public IAssetProvider? UiAssetProvider { get; init; }

    /// <summary>
    /// Gets the app's name, which the host writes into its page, as the page is every app's and names none.
    /// </summary>
    public required string AppName { get; init; }

    /// <summary>
    /// Gets the path of the app's logo in the UI's files (<see cref="AppOptions.LogoAssetPath"/>), which the host
    /// writes into its page.
    /// </summary>
    public required string LogoAssetPath { get; init; }

    /// <summary>
    /// Gets the look's name (<see cref="AppOptions.UiTheme"/>), whose colours the host reads from the UI's files and
    /// writes into its page.
    /// </summary>
    public required string UiTheme { get; init; }

    /// <summary>
    /// Gets the port the host prefers: the local host takes another where it is not free; the remote host keeps to it
    /// unless it can bind it on no address at all, and then takes a free one.
    /// </summary>
    public required int WebHostPort { get; init; }

    /// <summary>
    /// Gets a value indicating whether the host stays up for the life of the process, whether or not anything asked,
    /// and refuses to stop: <see langword="true"/> only for the remote host a developer opened.
    /// </summary>
    public required bool IsAlwaysOn { get; init; }

    /// <summary>
    /// Gets a value indicating whether the host asks a caller for its token: the remote host's pairing, or on the local
    /// host the token its own address carries, which an API call sends as a bearer token.
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> is the developer's open door: in a Debug build, and for the remote host with the
    /// <see cref="Utils.DebugCommands.RemoteAccess"/> command too, where there is no screen to read a pairing from.
    /// </remarks>
    public required bool IsTokenRequired { get; init; }
}
