using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.AppLib.Api.Premium;
using VpnHood.AppLib.Api.Sessions;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.AppLib.Api.App;

/// <summary>
/// Represents what the app is and offers on this device: its identity, its features and its links, which the UI reads
/// once.
/// </summary>
public class AppDescriptor
{
    /// <summary>Gets the app's ID, as the head names it, which the client's ID is made from.</summary>
    public required string AppId { get; init; }

    /// <summary>Gets the app's name.</summary>
    public required string AppName { get; init; }

    /// <summary>Gets the name without spaces that the release files, a desktop executable and its service, and the VPN adapter are called by.</summary>
    public required string PackageTitle { get; init; }

    /// <summary>Gets the maker's name, for a document that names it (<c>{companyName}</c>) as <see cref="AppName"/> is <c>{appName}</c>.</summary>
    public required string CompanyName { get; init; }

    /// <summary>Gets a value indicating whether the split tunneling by app can exclude apps on this device.</summary>
    public required bool IsExcludeAppsSupported { get; init; }

    /// <summary>Gets a value indicating whether the split tunneling by app can include apps on this device.</summary>
    public required bool IsIncludeAppsSupported { get; init; }

    /// <summary>Gets the theme's name, as <c>blue</c> or <c>violet</c>, which picks the UI's palette and its theme folder in the UI's store.</summary>
    public required string UiTheme { get; init; }

    /// <summary>Gets a value indicating whether the person may add an access key.</summary>
    public required bool IsAddAccessKeySupported { get; init; }

    /// <summary>Gets this build's premium tier, or <see langword="null"/> where the product has none.</summary>
    /// <remarks>
    /// <see langword="null"/> is the full app, not a locked one: every premium feature allowed, nothing sold, and no
    /// page that promotes or bills, whatever the server's client policy offers. The premium code and the purchase
    /// address exist only inside a tier.
    /// </remarks>
    public required AppPremiumOptions? Premium { get; init; }

    /// <summary>Gets a value indicating whether the app has an account backend, with its sign-in.</summary>
    public required bool IsAccountSupported { get; init; }

    /// <summary>Gets a value indicating whether the app has in-app purchasing.</summary>
    public required bool IsBillingSupported { get; init; }

    /// <summary>
    /// Gets the IDs of the sign-in methods the app offers (<see cref="VpnHood.AppLib.Api.Accounts.AuthProviders"/>), as its
    /// account backend declares them; none where it has no account backend.
    /// </summary>
    /// <remarks>
    /// They are strings, not an enum, as a third party's provider declares its own; the UI's label for each is
    /// <c>SIGN_IN_WITH_</c> and the ID in upper case.
    /// </remarks>
    public required IReadOnlyList<string> AuthProviderIds { get; init; }

    /// <summary>Gets a value indicating whether the person must accept the license agreement first.</summary>
    public required bool IsLicenseAgreementRequired { get; init; }

    /// <summary>Gets the app's links; a <see langword="null"/> link is one the UI hides.</summary>
    public required AppLinks Links { get; init; }

    /// <summary>Gets a value indicating whether the UI shows that the app is powered by VpnHood.</summary>
    public required bool ShowPoweredBy { get; init; }

    /// <summary>Gets the path of the logo in the UI's store.</summary>
    public required string LogoAssetPath { get; init; }

    /// <summary>Gets the name of the privacy consent's document in the UI's store, which the UI completes per language.</summary>
    public required string PrivacyConsentAssetName { get; init; }

    /// <summary>Gets a value indicating whether the app's VPN can use QUIC on this device.</summary>
    public required bool IsQuicSupported { get; init; }

    /// <summary>Gets how the app supports always-on VPN on this device.</summary>
    public required AlwaysOnSupport AlwaysOnSupport { get; init; }

    /// <summary>
    /// Gets a value indicating whether the app keeps a kill switch of its own, as a desktop's
    /// (<see cref="VpnHood.AppLib.Api.Settings.UserSettings.KillSwitch"/>).
    /// </summary>
    /// <remarks>
    /// Android's is the system's setting, which its page opens
    /// (<see cref="DeviceIntentFeatures.IsKillSwitchSettingsSupported"/>).
    /// </remarks>
    public required bool IsKillSwitchSupported { get; init; }

    /// <summary>
    /// Gets the path of the installer's script that lifts the kill switch's block with neither the service nor the app,
    /// which Linux's Kill Switch page prints as a command; <see langword="null"/> outside an install, and on Windows,
    /// whose page names the Start menu's entry.
    /// </summary>
    public required string? KillSwitchOffScriptPath { get; init; }

    /// <summary>
    /// Gets a value indicating whether the settings turn the automatic update on and off, as a desktop's own updater
    /// does (<see cref="IAppApi.GetAutomaticUpdate"/>); a store keeps its own setting.
    /// </summary>
    public required bool IsAutomaticUpdateSupported { get; init; }

    /// <summary>
    /// Gets a value indicating whether this build has the IP-location data a split by country needs; without it, a
    /// country mode fails the connect, so the UI hides it.
    /// </summary>
    public required bool IsSplitCountrySupported { get; init; }

    /// <summary>Gets a value indicating whether the app has a store review to ask for.</summary>
    public required bool IsUserReviewSupported { get; init; }

    /// <summary>
    /// Gets a value indicating whether this build collects anonymous data at all: usage events, and the crash reports
    /// that go with them where the tracker reports crashes.
    /// </summary>
    /// <remarks>
    /// It follows the tracker the app could make, so the UI drops the privacy consent in a build that collects nothing;
    /// the person's choice is <see cref="VpnHood.AppLib.Api.Settings.UserSettings.AllowAnonymousTracker"/>.
    /// </remarks>
    public required bool IsAnonymousTrackerSupported { get; init; }

    /// <summary>Gets or sets a value indicating whether the device is a TV, which the UI is drawn for.</summary>
    public required bool IsTv { get; set; }

    /// <summary>Gets the operating system the app runs on.</summary>
    public required AppOsType OsType { get; init; }

    /// <summary>Gets the client's ID, made from the app's ID and the device's or the install's own.</summary>
    public required string ClientId { get; init; }

    /// <summary>Gets a value indicating whether the app runs in its debug mode.</summary>
    public required bool IsDebugMode { get; init; }

    /// <summary>Gets the debug commands the app knows.</summary>
    public required IReadOnlyList<string> DebugCommands { get; init; }

    /// <summary>Gets a value indicating whether the app's VPN can reach its server through proxies.</summary>
    public required bool IsProxySupported { get; init; }

    /// <summary>
    /// Gets a value indicating whether the app has a listener a phone can pair with; without it, the remote access's
    /// calls throw, so the UI hides the pairing.
    /// </summary>
    public required bool IsRemoteAccessSupported { get; init; }

    /// <summary>Gets a value indicating whether the settings may choose how a server's addresses are ordered (<see cref="VpnHood.AppLib.Api.Settings.EndPointStrategy"/>).</summary>
    public required bool AllowEndPointStrategy { get; init; }

    /// <summary>Gets or sets a value indicating whether the app shows ads.</summary>
    public required bool IsAdSupported { get; set; }

    /// <summary>Gets a value indicating whether the app has rewarded ads.</summary>
    public required bool IsRewardedAdSupported { get; init; }

    /// <summary>Gets the channel protocols the app's VPN can use on this device.</summary>
    public required IReadOnlyList<ChannelProtocol> ChannelProtocols { get; init; }

    /// <summary>Gets the JSON the head hands the UI uninterpreted, as a product's own settings, or <see langword="null"/>.</summary>
    public required JsonElement? CustomData { get; init; }

    /// <summary>Gets the app's version.</summary>
    [JsonConverter(typeof(VersionConverter))]
    public required Version Version { get; init; }
}