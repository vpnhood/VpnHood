using System.Text.Json;
using VpnHood.AppLib.Api.App;

namespace VpnHood.AppLib.App.Utils;

/// <summary>
/// Provides what a product's private settings file can say (<see cref="AppConfigsLoader"/>); a product derives from
/// it for keys of its own. The options of the same names (<see cref="AppOptions"/>) say what each means.
/// </summary>
public class AppConfigs
{
    /// <summary>Gets or sets the pages the build links to (<see cref="AppOptions.Links"/>).</summary>
    public AppLinks Links { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the menu shows the engine's credit
    /// (<see cref="AppOptions.ShowPoweredBy"/>). The default is <see langword="true"/>.
    /// </summary>
    public bool ShowPoweredBy { get; set; } = true;

    /// <summary>Gets or sets the look's name (<see cref="AppOptions.UiTheme"/>). The default is <c>blue</c>.</summary>
    public string UiTheme { get; set; } = "blue";

    /// <summary>
    /// Gets or sets the web host's port, or <see langword="null"/> for the default
    /// (<see cref="AppOptions.WebHostPort"/>).
    /// </summary>
    public int? WebHostPort { get; set; }

    /// <summary>
    /// Gets or sets the address of the settings the app reads from the web, or <see langword="null"/>
    /// (<see cref="AppOptions.RemoteSettingsUrl"/>).
    /// </summary>
    public Uri? RemoteSettingsUrl { get; set; }

    /// <summary>
    /// Gets or sets the Google Analytics 4 measurement ID the head's tracker reports to, or <see langword="null"/>.
    /// </summary>
    public string? Ga4MeasurementId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the trackers may get the servers' endpoints
    /// (<see cref="AppOptions.AllowEndPointTracker"/>).
    /// </summary>
    public bool AllowEndPointTracker { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the person may choose the endpoint strategy
    /// (<see cref="AppOptions.AllowEndPointStrategy"/>). The default is <see langword="true"/>.
    /// </summary>
    public bool AllowEndPointStrategy { get; set; } = true;

    /// <summary>Gets or sets the access key the product is built with, or <see langword="null"/>.</summary>
    public string? DefaultAccessKey { get; set; }

    /// <summary>
    /// Gets or sets JSON for a UI of the product's own (<see cref="AppOptions.CustomData"/>), or
    /// <see langword="null"/>.
    /// </summary>
    public JsonElement? CustomData { get; set; }

    /// <summary>
    /// Gets or sets the address of the folder of the heads' update feeds (<see cref="GetUpdateInfoUrl"/>), or
    /// <see langword="null"/> for none.
    /// </summary>
    public Uri? UpdateInfoBaseUrl { get; set; }

    /// <summary>
    /// Returns a head's update feed in <see cref="UpdateInfoBaseUrl"/>, named as the publisher names it, such as
    /// <c>VpnHoodClient-win-x64.json</c>.
    /// </summary>
    /// <param name="packageTitle">The app's name without spaces (<see cref="AppOptions.PackageTitle"/>).</param>
    /// <param name="distribution">The head's distribution, such as <c>win-x64</c>.</param>
    /// <returns>
    /// The feed's address, <i>packageTitle</i><c>-</c><i>distribution</i><c>.json</c> in
    /// <see cref="UpdateInfoBaseUrl"/>, or <see langword="null"/> when <see cref="UpdateInfoBaseUrl"/> is.
    /// </returns>
    /// <exception cref="InvalidOperationException"><see cref="UpdateInfoBaseUrl"/> is not an absolute URI.</exception>
    public Uri? GetUpdateInfoUrl(string packageTitle, string distribution) => UpdateInfoBaseUrl is null
        ? null
        : new Uri($"{UpdateInfoBaseUrl.AbsoluteUri.TrimEnd('/')}/{packageTitle}-{distribution}.json");
}
