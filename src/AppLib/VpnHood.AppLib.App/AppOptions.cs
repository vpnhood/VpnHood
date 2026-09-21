using System.Text.Json;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Abstractions.Device;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Premium;
using VpnHood.AppLib.Services.Ads;
using VpnHood.AppLib.Services.Updaters;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Core.Toolkit.Assets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.AppLib.WebHosting;

namespace VpnHood.AppLib;

public class AppOptions(string appId, string storageFolderName, bool isDebugMode)
{
    public static string BuildStorageFolderPath(string subFolder)
    {
        // default
        var baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (OperatingSystem.IsLinux()) {
            // get current executable folder
            baseFolder = Path.GetDirectoryName(Environment.ProcessPath!)!;
        }

        return Path.Combine(baseFolder, subFolder);
    }

    public string AppId => appId;
    public bool IsDebugMode => isDebugMode;

    // Tests run many concurrent apps in one process, so they opt out of the singleton
    // registration; production keeps the single-instance guarantee and VpnHoodApp.Instance.
    internal bool IsSingleton { get; set; } = true;
    public string StorageFolderPath { get; set; } = BuildStorageFolderPath(storageFolderName);

    // Transport tuning handed to the client untouched. Its timeouts default themselves; the buffer
    // and count knobs stay null so each core component applies its own default at first use.
    // Starts at the preset for this platform, so a memory-capped head is safe without opting in.
    public ClientTransportOptions Transport { get; set; } = ClientTransportOptions.ForCurrentPlatform();
    public AppUpdaterOptions? UpdaterOptions { get; set; }
    // What the ENGINE reads of a head's identity. The look the OS chrome draws with - the colours,
    // the tray icons - is read out of the UI's store (AppBranding, under UiTheme), not handed in.
    public required string AppName { get; init; }

    // Whose app this is: the maker's name, for the words that name it - {companyName} in a consent
    // summary or any content document - as AppName is {appName}. Ours say VpnHood; a fork says itself.
    public required string CompanyName { get; init; }

    // The ~14 MB IP-location db the head ships, wherever its platform placed it. Opened only when a
    // country split or a location lookup actually runs, never at startup, and opened afresh each
    // time - the readers dispose what they are given. Null means the head shipped no database.
    public IAsset? IpLocationZipAsset { get; set; }

    // The files the app's UI draws from - pictures, fonts, words - if the head has any: the asset
    // package's zips, wherever its platform placed them. The app extracts each and shares ONE
    // provider over all of them: the web host serves it at /assets/ to a paired phone's page, and
    // the in-process UI reads it directly. Empty for a head whose UI brings nothing of its own.
    // Searched in the order given, and the first zip that has the file wins - so a head that ships
    // a zip of its own BEFORE the UI's replaces single files of it: a fork's logo, its consent
    // summary, a picture, without a UI build. Ours name one, the UI's store.
    public IReadOnlyList<IAsset> UiZipAssets { get; set; } = [];

    // The page the web host serves - the UI's browser build - as the zip the head's
    // platform placed beside it, wherever that is; the app extracts it as it does the UI's. Carried
    // here rather than configured on the factory, so both of the host's files travel one path and
    // are named once; the app itself never reads it. Required by a head that sets WebHostFactory.
    public IAsset? WebRootZipAsset { get; set; }

    // ReSharper disable once StringLiteralTypo
    public string? Ga4MeasurementId { get; set; } = "G-4LE99XKZYE";
    // The look, as the UI's store carries it: "blue" or "violet" - the theme's own name, never a
    // product's, since what a product IS is the features above. It picks the palette the UI draws
    // and branding/<theme>/ in the store for the OS chrome.
    public string UiTheme { get; set; } = "blue";
    public bool IsAddAccessKeySupported { get; set; } = true;

    // This build's premium tier, or null when the product has none (the CLIENT apps): the app then
    // runs as the FULL app — every feature on, nothing sold, no promotion — however the server's
    // client policies tempt it. See AppPremiumOptions for the per-member rules.
    public AppPremiumOptions? Premium { get; set; }
    public string[] AccessKeys { get; set; } = [];
    public IDeviceUiProvider? DeviceUiProvider { get; set; }
    public IAppCultureProvider? CultureProvider { get; set; }
    public IAccountProvider? AccountProvider { get; set; }
    public IAppUserReviewProvider? UserReviewProvider { get; set; }
    public IReadOnlyList<AppAdProviderItem> AdProviderItems { get; set; } = [];
    public ITrackerFactory? TrackerFactory { get; set; }

    // What a paired phone loads, built on the app itself - so the head
    // hands in a factory rather than an instance. Null means this head runs no web host, and
    // AppFeatures.IsRemoteAccessSupported says so before a UI offers the pairing screen.
    public IAppWebHostFactory? WebHostFactory { get; set; }

    public bool? LogAnonymous { get; set; } =
        isDebugMode ? false : null; // it follows user's settings if it set to null

    // The whole-connect deadline for the app (tripled when diagnosing) - not the per-TCP connect
    // timeout, which is Transport.TcpConnectTimeout.
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromMinutes(4).WhenNoDebugger();
    public bool AutoDiagnose { get; set; } = true;
    public AppAdOptions AdOptions { get; set; } = new();
    public bool AllowEndPointTracker { get; set; }
    public string? DeviceId { get; set; }
    public TimeSpan? EventWatcherInterval { get; set; } // set if you don't call State periodically
    public bool DisconnectOnDispose { get; set; }
    public LogServiceOptions LogServiceOptions { get; set; } = new();
    public bool AdjustForSystemBars { get; set; } = true;
    public bool AllowEndPointStrategy { get; set; }
    // JSON the head hands the UI, uninterpreted: the product's own settings (firebaseOptions and
    // friends) straight out of its appsettings. JsonElement, not object - the contract is serialized
    // through a source-generated context on trimmed heads, and "object" means "whatever the head
    // happened to put here", which is how one head ended up passing a JsonObject and another a
    // JsonElement for the same field.
    public JsonElement? CustomData { get; set; }
    public bool AllowRecommendUserReviewByServer { get; set; }
    public Uri? RemoteSettingsUrl { get; set; }

    // The two legal documents this build links to - from the paywall, from Settings > Privacy, and
    // from the first-run screen where that is shown. Every head fills them in from its
    // appsettings.json, exactly like Ga4MeasurementId and RemoteSettingsUrl above, so a fork points
    // at its own documents without editing code. The App Store heads are the one exception: they
    // hardcode TermsOfUseUrl to Apple's standard EULA, the agreement actually governing a purchase
    // made there while no custom EULA is registered with Apple.
    // Null means the build ships no such document, and the UI hides the link rather than guess an
    // address. Keep this the only place either URL is set: a second source would have to be resolved
    // against this one, and the symptom of getting that wrong is the wrong EULA on a paywall, found
    // by a store rejection rather than by a test.
    public Uri? PrivacyPolicyUrl { get; set; }
    public Uri? TermsOfUseUrl { get; set; }

    // The two things the UI shows that carry the product's own word, named by the head as the URLs
    // above are, and neither the look's (UiTheme) to decide: a fork keeps our violet under its own
    // name and its own promises.
    //
    // Both address the UI's store, and the suffix says how completely. PATH is the whole thing, the
    // string a provider takes ("images/VpnHoodConnect-logo.png"), so a fork may keep its logo
    // anywhere in the store. NAME is the part the head chooses ("privacy-consent-connect") and the
    // UI completes: content/<lang>/<name>.md, because the consent summary is one asset per language
    // and a language never translated has to fall back to English.
    //
    // Required here and again in each head's config (IRequiredAppConfigs): a connect head that
    // forgot would show the client's promises on a consent screen, which no test finds.
    public required string LogoAssetPath { get; init; }
    public required string PrivacyConsentAssetName { get; init; }

    // Whether this build must have its licence agreement accepted before the app can be used. A
    // DISTRIBUTION decision, not a product one: a website download passed through nothing that put
    // our terms in front of the user, while a store build's user already accepted that store's own
    // agreement to install it. The screen is that channel's substitute for a store, not an extra
    // requirement on top of one - it is the links above that App Review 3.1.2 and Play's User Data
    // policy actually ask for.
    // On by default and opted OUT of by the four store heads, deliberately that way round: a head
    // added later that forgets the line shows one screen too many, which is noise, rather than
    // skipping a disclosure, which is a gap.
    // The user's answer is UserSettings.IsLicenseAccepted, asked once.
    public bool IsLicenseAgreementRequired { get; set; } = true;
    public int? WebUiPort { get; set; }
}
