using System.Text.Json;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Abstractions.Device;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Premium;
using VpnHood.AppLib.App.Services.Ads;
using VpnHood.AppLib.App.Services.AlwaysOn;
using VpnHood.AppLib.App.Services.Updaters;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Storages;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.AppLib.App.WebHosting;

namespace VpnHood.AppLib.App;

/// <summary>
/// Provides the options a head builds the app with (<see cref="VpnHoodApp.Init"/>): who the app is, what it offers,
/// the providers it works through, and how it behaves.
/// </summary>
/// <param name="context">
/// What the platform has settled before the head builds the options: the app's ID, name and version, and its
/// storage.
/// </param>
/// <param name="isDebugMode">
/// <see langword="true"/> for a Debug build, which logs at the debug level and never anonymously, and reports to no
/// tracker; by default, it also adds <c>_dbg</c> to its adapter's name and takes port 9091 for its web host, so it runs
/// beside an installed release.
/// </param>
/// <remarks>
/// The app's ID, name, version and storage come from <paramref name="context"/> and cannot be set here: a desktop
/// platform has used the ID and the storage, for its single-instance lock and its files, before it builds the
/// options.
/// </remarks>
public class AppOptions(AppOptionsContext context, bool isDebugMode)
{
    /// <summary>Gets the app's ID on the device (<see cref="AppOptionsContext.AppId"/>).</summary>
    public string AppId => context.AppId;

    /// <summary>Gets a value indicating whether this is a Debug build.</summary>
    public bool IsDebugMode => isDebugMode;

    // Tests run many concurrent apps in one process, so they opt out of the singleton
    // registration; production keeps the single-instance guarantee and VpnHoodApp.Instance.
    internal bool IsSingleton { get; set; } = true;

    /// <summary>
    /// Gets the folder that holds the settings and the VPN profiles (<see cref="AppOptionsContext.StoragePath"/>).
    /// </summary>
    public string StorageFolderPath => context.StoragePath;

    /// <summary>
    /// Gets the folder for what must not travel with a backup: the sign-in session, the log and the caches
    /// (<see cref="AppOptionsContext.NoBackupStoragePath"/>).
    /// </summary>
    public string NoBackupStorageFolderPath => context.NoBackupStoragePath;

    /// <summary>
    /// Gets the storage over <see cref="StorageFolderPath"/> (<see cref="AppOptionsContext.Storage"/>).
    /// </summary>
    public Storage Storage => context.Storage;

    /// <summary>
    /// Gets the storage over <see cref="NoBackupStorageFolderPath"/> (<see cref="AppOptionsContext.NoBackupStorage"/>).
    /// </summary>
    public Storage NoBackupStorage => context.NoBackupStorage;

    // tvOS empties the VPN service's data folder - in the App Group's Caches, the only folder there an app
    // may write - when the device runs low on storage, so the proxies the person added are kept in the
    // storage too (ProxyEndPointCopy). Tests set it to see that work off a TV.
    internal bool IsVpnServiceDataPurgeable { get; set; } = OperatingSystem.IsTvOS();

    /// <summary>
    /// Gets or sets the transport's tuning, which the app hands to the VPN client as it is. The default is this
    /// platform's preset (<see cref="ClientTransportOptions.ForCurrentPlatform"/>), a limited one on iOS and tvOS.
    /// </summary>
    public ClientTransportOptions Transport { get; set; } = ClientTransportOptions.ForCurrentPlatform();

    /// <summary>
    /// Gets or sets how the app looks for its updates, or <see langword="null"/> for an app that does not.
    /// </summary>
    public AppUpdaterOptions? UpdaterOptions { get; set; }

    /// <summary>Gets the name people see (<see cref="AppOptionsContext.AppName"/>).</summary>
    public string AppName => context.AppName;

    /// <summary>Gets the app's version (<see cref="AppOptionsContext.AppVersion"/>).</summary>
    public Version AppVersion => context.AppVersion;

    /// <summary>
    /// Gets the app's name without spaces, which its release files, a desktop executable and its service, and the VPN
    /// adapter are named by.
    /// </summary>
    public required string PackageTitle { get; init; }

    /// <summary>
    /// Gets the maker's name, which the app's words put in place of <c>{companyName}</c>, as they put
    /// <see cref="AppName"/> in place of <c>{appName}</c>.
    /// </summary>
    public required string CompanyName { get; init; }

    /// <summary>
    /// Gets or sets the VPN adapter's name where the system shows one, a Windows adapter or a Linux interface, or
    /// <see langword="null"/> for <see cref="PackageTitle"/>, with <c>_dbg</c> in a Debug build.
    /// </summary>
    public string? AdapterName { get; set; }

    /// <summary>
    /// Gets or sets the IP-location database the head ships, a zip, or <see langword="null"/> for none, and then the
    /// app offers no split by country and looks up no location.
    /// </summary>
    /// <remarks>
    /// The app opens it when it builds a split by country or looks a location up, as the reconfigure at its start may
    /// do.
    /// </remarks>
    public IAsset? IpLocationZipAsset { get; set; }

    /// <summary>
    /// Gets or sets the zips of the files the app's UI draws from, such as pictures, fonts and words; empty for a UI
    /// that brings none of its own. The default is empty.
    /// </summary>
    /// <remarks>
    /// The app unpacks each into its device-only storage and reads them as one store
    /// (<see cref="VpnHoodApp.UiAssetProvider"/>), which its web host serves to a paired phone too. A file is taken
    /// from the first zip that has it, so a zip of the head's own placed before the UI's replaces single files of it,
    /// such as a logo, without a UI build.
    /// </remarks>
    public IReadOnlyList<IAsset> UiZipAssets { get; set; } = [];

    /// <summary>
    /// Gets or sets the page the web host serves, the UI's browser build, as a zip, or <see langword="null"/> for none.
    /// A head that sets <see cref="WebHostFactory"/> must set it too.
    /// </summary>
    public IAsset? WebRootZipAsset { get; set; }

    /// <summary>
    /// Gets or sets the look's name in the UI's store, such as <c>blue</c> or <c>violet</c>, which picks the palette
    /// the UI draws with and the store's <c>themes/</c> folder of the same name. The default is <c>blue</c>.
    /// </summary>
    public string UiTheme { get; set; } = "blue";

    /// <summary>
    /// Gets or sets a value indicating whether a person may add an access key in the app
    /// (<see cref="AppDescriptor.IsAddAccessKeySupported"/>). The default is <see langword="true"/>.
    /// </summary>
    public bool IsAddAccessKeySupported { get; set; } = true;

    /// <summary>
    /// Gets or sets this build's premium tier, or <see langword="null"/> for a product that has none, which then runs
    /// as the full app: every feature allowed, with no tier to sell.
    /// </summary>
    public AppPremiumOptions? Premium { get; set; }

    /// <summary>
    /// Gets or sets the access keys the app is built with. The default is none.
    /// </summary>
    /// <remarks>
    /// At its start the app adds each as a built-in VPN profile, which the person cannot overwrite by importing its
    /// key, and removes the built-in profiles of keys no longer listed. The first is selected when no profile is.
    /// </remarks>
    public string[] AccessKeys { get; set; } = [];

    /// <summary>
    /// Gets or sets what the app asks of the device's own UI, such as notifications, Quick Launch and the system bars,
    /// or <see langword="null"/> for a <see cref="NullDeviceUiProvider"/>, which supports none of it but takes a web
    /// browser as present.
    /// </summary>
    public IDeviceUiProvider? DeviceUiProvider { get; set; }

    /// <summary>
    /// Gets or sets how the app reads and sets its language, or <see langword="null"/> for the app's own, which keeps
    /// it in the settings.
    /// </summary>
    public IAppCultureProvider? CultureProvider { get; set; }

    /// <summary>
    /// Gets or sets the account backend, and its store through <see cref="IAccountProvider.Billing"/>, or
    /// <see langword="null"/> for an app with no accounts.
    /// </summary>
    public IAccountProvider? AccountProvider { get; set; }

    /// <summary>
    /// Gets or sets the store's in-app review, or <see langword="null"/> for none, and then the app never asks for a
    /// review.
    /// </summary>
    public IAppUserReviewProvider? UserReviewProvider { get; set; }

    /// <summary>
    /// Gets or sets the ad networks the app shows ads from, tried in this order. The default is none, and so is the
    /// value an engine without ads (<see cref="VpnEngineFeatures.Ads"/>) leaves the app with.
    /// </summary>
    public IReadOnlyList<AppAdProviderItem> AdProviderItems { get; set; } = [];

    /// <summary>
    /// Gets or sets the factories of the trackers the app and its VPN service report through, one tracker from each.
    /// The default is none.
    /// </summary>
    /// <remarks>The app decides when they report, and a Debug build makes none.</remarks>
    public IReadOnlyList<ITrackerFactory> TrackerFactories { get; set; } = [];

    /// <summary>
    /// Gets or sets the VPN engine this head bundles, as it reports itself: the app writes the options in a version
    /// both speak, and offers only what the engine has. The default is this library's engine.
    /// </summary>
    public VpnEngineInfo VpnEngineInfo { get; set; } = VpnEngineInfo.VpnHoodEngine;

    /// <summary>
    /// Gets or sets how the app makes its web hosts, the one this device loads its UI from and the one a phone pairs
    /// with, or <see langword="null"/> for an app with neither, which then supports no remote access
    /// (<see cref="AppDescriptor.IsRemoteAccessSupported"/>).
    /// </summary>
    public IAppWebHostFactory? WebHostFactory { get; set; }

    /// <summary>
    /// Gets or sets a value the app does not read. The default is <see langword="false"/> in a Debug build and
    /// <see langword="null"/> otherwise.
    /// </summary>
    /// <remarks>
    /// Whether the log hides addresses comes from <see cref="LogServiceOptions"/>'s
    /// <see cref="VpnHood.Net.Toolkit.Logging.LogServiceOptions.LogAnonymous"/> and the person's
    /// <see cref="VpnHood.AppLib.Api.Settings.UserSettings.LogAnonymous"/>: anonymous where either is on, and never in
    /// a Debug build.
    /// </remarks>
    public bool? LogAnonymous { get; set; } =
        isDebugMode ? false : null;

    /// <summary>
    /// Gets or sets how long a whole connect may take, three times as long when it diagnoses; not a single TCP
    /// connect's, which is <see cref="Transport"/>'s. The default is four minutes.
    /// </summary>
    /// <remarks>
    /// While a debugger is attached, <see cref="VhUtils.DebuggerTimeout"/> takes its place where it is set.
    /// </remarks>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromMinutes(4).WhenNoDebugger();

    /// <summary>
    /// Gets or sets a value indicating whether a connect that fails checks the network on its own, so an error that
    /// comes from no internet says so. The default is <see langword="true"/>.
    /// </summary>
    /// <remarks>Not after a cancel, a server's refusal or a connect its caller tries again.</remarks>
    public bool AutoDiagnose { get; set; } = true;

    /// <summary>Gets or sets how the app loads and shows its ads.</summary>
    public AppAdOptions AdOptions { get; set; } = new();

    /// <summary>Gets or sets how always on tries again after a failure.</summary>
    public AppAlwaysOnOptions AlwaysOnOptions { get; set; } = new();

    /// <summary>
    /// Gets or sets the path of the installer's script that lifts the kill switch's block with neither the service nor
    /// the app running, which the Kill Switch page prints as a command; <see langword="null"/> outside an install. The
    /// Linux platform sets it.
    /// </summary>
    public string? KillSwitchOffScriptPath { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the trackers may get the availability of each server endpoint the client
    /// checks (<see cref="ClientOptions.AllowEndPointTracker"/>), where the person allows usage data. The default is
    /// <see langword="false"/>.
    /// </summary>
    public bool AllowEndPointTracker { get; set; }

    /// <summary>
    /// Gets or sets the device's own ID, which the client ID is made from, or <see langword="null"/> for the install's
    /// own random ID.
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// Gets or sets how often the app reads the VPN service's state in the background, for a head that does not read
    /// <see cref="VpnHoodApp.State"/> often, or <see langword="null"/> for no reading beyond the first.
    /// </summary>
    public TimeSpan? EventWatcherInterval { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether disposing the app with <see cref="VpnHoodApp.DisposeAsync"/> disconnects
    /// the VPN; <see cref="VpnHood.Net.Toolkit.Utils.Singleton{T}.Dispose()"/> does not. The default is
    /// <see langword="false"/>, which leaves the VPN service connected.
    /// </summary>
    public bool DisconnectOnDispose { get; set; }

    /// <summary>
    /// Gets or sets the log's options. A Debug build logs at the debug level at least, and the person's debug commands
    /// may lower the level further.
    /// </summary>
    public LogServiceOptions LogServiceOptions { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the person may choose how the client picks the server's endpoints, where
    /// the engine has it too (<see cref="AppDescriptor.AllowEndPointStrategy"/>). The default is
    /// <see langword="true"/>.
    /// </summary>
    public bool AllowEndPointStrategy { get; set; } = true;

    /// <summary>
    /// Gets or sets JSON the head hands a UI of its own, which the app does not read
    /// (<see cref="AppDescriptor.CustomData"/>), or <see langword="null"/>.
    /// </summary>
    public JsonElement? CustomData { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the app asks for a review when the server recommends it during a session
    /// that ended well, where the app has a review provider. The default is <see langword="false"/>.
    /// </summary>
    public bool AllowRecommendUserReviewByServer { get; set; }

    /// <summary>
    /// Gets or sets the address of the settings the app reads from the web at each start, such as its promotion, or
    /// <see langword="null"/> for none.
    /// </summary>
    public Uri? RemoteSettingsUrl { get; set; }

    /// <summary>
    /// Gets or sets the pages this build links to: its legal documents and the menu's links.
    /// </summary>
    /// <remarks>
    /// Our heads fill them in from their product's settings (<see cref="VpnHood.AppLib.App.Utils.AppConfigs.Links"/>),
    /// so a fork points at its own pages without changing code; the App Store heads set the terms of use to Apple's
    /// standard licence agreement.
    /// </remarks>
    public AppLinks Links { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the menu shows the engine's credit, Powered by VpnHood! ENGINE. The
    /// default is <see langword="true"/>.
    /// </summary>
    public bool ShowPoweredBy { get; set; } = true;

    /// <summary>
    /// Gets the path of the product's logo in the UI's store, such as <c>images/logo-connect.png</c>.
    /// </summary>
    public required string LogoAssetPath { get; init; }

    /// <summary>
    /// Gets the name of the product's consent summary in the UI's store, such as <c>privacy-consent-connect</c>, which
    /// the UI reads as <c>content/</c><i>language</i><c>/</c><i>name</i><c>.md</c>, in English where a language has
    /// none.
    /// </summary>
    public required string PrivacyConsentAssetName { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the person must accept the licence agreement before using the app, once.
    /// The default is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// A matter of distribution: a download from a website passed through nothing that showed the terms, while a
    /// store's install did. The Google Play heads turn it off; the App Store heads keep it, as App Review wants a VPN
    /// app to say what it collects before first use. The answer is
    /// <see cref="VpnHood.AppLib.Api.Settings.UserSettings.IsLicenseAccepted"/>.
    /// </remarks>
    public bool IsLicenseAgreementRequired { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether usage data waits for the person's own yes in every region, as it does
    /// where the law wants one: the first-run page then asks with the box unticked everywhere. The default is
    /// <see langword="false"/>.
    /// </summary>
    public bool IsAnonymousTrackerOptInRequired { get; set; }

    /// <summary>
    /// Gets or sets the web host's port, which a paired phone's address names, or <see langword="null"/> for 9090, and
    /// 9091 in a Debug build, so it runs beside the installed app.
    /// </summary>
    public int? WebHostPort { get; set; }
}
