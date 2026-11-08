using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Accounts;
using VpnHood.AppLib.Api.VpnProfiles;
using VpnHood.AppLib.Api.Sessions;
using VpnHood.AppLib.Api.Settings;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Common;

// What the pages read off the app, and the one way they reach it: the app's API (VpnHoodApi), the same
// six interfaces in process - the web server's controllers, no listener - and over HTTP, from a
// paired browser. The pages read plain values held here - the features, the state, the settings,
// the profiles - and never the app itself, so the same pages run on the device and in a browser.
// The values are read again through the API: the state every second by the home's clock and after
// every action, the rest when the state says the configuration moved (AppState.ConfigTime)
// or a page saved, and the account when the state says it changed (AppState.AccountTime).
public static class VhApp
{
    private static VpnHoodApi? _api;
    private static AppInfo? _info;
    private static AppState? _state;
    private static IReadOnlyList<string> _availableCultures = [];
    private static string? _sentCultureName;

    public static VpnHoodApi Api => _api ?? throw new InvalidOperationException(
        $"The UI has not been given the app's API. A head must call {nameof(VhApp)}.{nameof(Init)} before the UI starts.");

    public static bool IsInit => _api != null;
    public static bool IsConfigured { get; private set; }

    // The head's first step, before Avalonia starts: the API, and the app's info read through
    // it - the features decide the theme, which is applied as the application initializes. In
    // process the read completes at once. This and Configure configure their awaits away (Vhc): the
    // Android host blocks its main thread on both (AvaloniaUiHosting). The read is this method's own,
    // not ReloadInfo's: the pages call that one, and it stores its values on their thread.
    public static async Task Init(VpnHoodApi api, CancellationToken cancellationToken)
    {
        _api = api;
        // another app's account is not this one's: nothing read yet, nothing to wait for, and a read
        // still on its way from the old API is older than any read from this one
        Account = null;
        _accountReadShown = ++_accountReadCount;
        _accountTime = null;
        _accountRetryTime = 0;
        _accountReadFailed = false;
        _info = await Api.App.GetInfo(cancellationToken).ConfigureAwait(false);
        _state = _info.State;
    }

    // The head's second step, once the UI can say which languages it has: they are declared to
    // the app, so the app's language list and its
    // best-culture choice are made from the words that exist. A UI whose words live in files it
    // must first place has placed them by now - that moment is the head's, not a page's.
    public static async Task Configure(IReadOnlyList<string> availableCultures, CancellationToken cancellationToken)
    {
        _availableCultures = availableCultures;
        _info = await Api.App.Configure(CreateConfigParams(), cancellationToken).ConfigureAwait(false);
        _state = _info.State;

        // the words again when the language changes
        if (!IsConfigured)
            Strings.Current.PropertyChanged += (_, _) => _ = SendWords(CancellationToken.None);
        IsConfigured = true;
    }

    // The app's own words - Android's VPN notification and messages - in the language this UI shows,
    // which is the device's on a paired browser too: it takes it from the app's settings.
    private static ConfigParams CreateConfigParams()
    {
        _sentCultureName = Strings.Current.CultureName;
        return new ConfigParams {
            AvailableCultures = [.. _availableCultures],
            Strings = Strings.Current.Texts
        };
    }

    // Once per language: configuring the app applies its settings again, which reconfigures a session.
    private static async Task SendWords(CancellationToken cancellationToken)
    {
        if (Strings.Current.CultureName == _sentCultureName)
            return;

        try {
            await Api.App.Configure(CreateConfigParams(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not give the app the words of the UI's language.");
        }
    }

    private static AppInfo Info => _info ?? throw new InvalidOperationException(
        $"The app's info has not been read. {nameof(VhApp)}.{nameof(Init)} reads it.");

    public static AppDescriptor Descriptor => Info.Descriptor;
    public static DeviceIntentFeatures Intents => Info.IntentFeatures;
    public static UserSettings UserSettings => Info.UserSettings;
    public static IReadOnlyList<VpnProfileInfo> VpnProfileInfos => Info.VpnProfileInfos;
    public static IReadOnlyList<UiCultureInfo> AvailableCultureInfos => Info.AvailableCultureInfos;
    public static AppState State => _state ?? Info.State;

    // Whether this UI is the remote: served by the app to a browser on another device, which is
    // what the API says of the request that read the configuration. Never
    // on the device itself.
    public static bool IsRemote => Info.IsRemote;

    // The profile the app is set to, whole (its locations), off the last configuration read; the
    // state carries its base info.
    public static VpnProfileInfo? CurrentVpnProfileInfo => FindVpnProfileInfo(UserSettings.VpnProfileId);

    public static VpnProfileInfo? FindVpnProfileInfo(Guid? vpnProfileId)
    {
        return vpnProfileId == null
            ? null
            : VpnProfileInfos.FirstOrDefault(x => x.VpnProfileId == vpnProfileId);
    }

    public static async Task ReloadState(CancellationToken cancellationToken)
    {
        var state = await Api.App.GetState(cancellationToken);
        var configMoved = _state != null && state.ConfigTime != _state.ConfigTime;
        _state = state;
        if (configMoved)
            await ReloadInfo(cancellationToken);

        // a sign-in or sign-out from another UI: a paired browser's reaches the TV's own screen
        if (state.AccountTime != _accountTime && Environment.TickCount64 >= _accountRetryTime)
            await ReloadAccount(cancellationToken);
    }

    // The account read the state asks for. Its failure is not the state's: the page still shows the
    // state, and the account is tried again after a pause, as the app asks the portal for an account it
    // holds no copy of. A read cut short by the state's own timeout pauses too, or a slow portal would
    // be asked again every second.
    private static async Task ReloadAccount(CancellationToken cancellationToken)
    {
        try {
            await LoadAccount(false, cancellationToken);
            _accountReadFailed = false;
        }
        catch (Exception ex) {
            _accountRetryTime = Environment.TickCount64 + (long)AccountRetryDelay.TotalMilliseconds;
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                throw;

            VhLogger.Instance.Log(_accountReadFailed ? LogLevel.Debug : LogLevel.Warning, ex,
                "Could not read the account. It is read again in a while.");
            _accountReadFailed = true;
        }
    }

    public static async Task ReloadInfo(CancellationToken cancellationToken)
    {
        _info = await Api.App.GetInfo(cancellationToken);
        _state = _info.State;
    }

    // The settings, written as one and read again: the app applies them as it saves, and may have
    // corrected a value. The object is the caller's - the one it read here and changed - so a
    // configuration read in between (the clock's) cannot lose the change.
    public static async Task SaveUserSettings(UserSettings userSettings, CancellationToken cancellationToken)
    {
        await Api.App.SetUserSettings(userSettings, cancellationToken);
        await ReloadInfo(cancellationToken);
    }

    // The account: read at start, after this UI's sign-in, sign-out or purchase, and when the state
    // says it changed - never polled itself.
    public static Account? Account { get; private set; }

    // the state's AccountTime as of the account shown, taken before its read: a change during the read
    // is read again at the next state
    private static DateTime? _accountTime;

    // the reads started, and the newest whose answer is shown: an older read answering after it, as
    // HTTP may, would put back the account it replaced. All on the pages' thread.
    private static int _accountReadCount;
    private static int _accountReadShown;

    internal static TimeSpan AccountRetryDelay { get; set; } = TimeSpan.FromSeconds(10);
    private static long _accountRetryTime;
    private static bool _accountReadFailed;

    public static async Task LoadAccount(bool withRefresh, CancellationToken cancellationToken)
    {
        var accountTime = State.AccountTime;
        var readId = ++_accountReadCount;
        if (!Descriptor.IsAccountSupported) {
            ShowAccount(readId, null, accountTime);
            return;
        }

        if (withRefresh) {
            try {
                await Api.Account.Refresh(cancellationToken);
            }
            catch (Exception) {
                // best effort: the portal is often what is blocked, and the saved account still shows
            }
        }

        var account = await Api.Account.Get(cancellationToken);
        ShowAccount(readId, account, accountTime);
    }

    private static void ShowAccount(int readId, Account? account, DateTime? accountTime)
    {
        if (readId < _accountReadShown)
            return;

        _accountReadShown = readId;
        Account = account;
        _accountTime = accountTime;
    }

    // The TV layout: the device's word, unless this UI is the remote.
    // A phone driving a TV gets the phone's layout.
    public static bool IsTvUi => Descriptor.IsTv && !IsRemote;

    // A browser driving a TV: what the TV's own screen asks - a store's sheet, Android's VPN prompt,
    // an ad - is answered there, so a wait for it says to go on there.
    public static bool IsTvRemote => Descriptor.IsTv && IsRemote;

    // One built-in profile, called "location", against a list of servers the user adds keys for:
    // the list is fixed exactly when no key can be added.
    public static bool IsSingleProfileMode => !Descriptor.IsAddAccessKeySupported;

    public static bool IsConnected(AppState state)
    {
        return state.ConnectionState is AppConnectionState.Connected or AppConnectionState.Unstable;
    }

    public static bool IsConnected() => IsConnected(State);

    // A build with no premium tier is the full app: nothing sold, no crown, everything allowed. Left
    // so: a public key whose own plans include premium still shows them there (the locations' crowns,
    // the countdown, the promote page), since they describe that key.
    public static bool IsPremiumSupported => Descriptor.Premium != null;
    public static bool IsPremiumUser => State.VpnProfile?.IsPremium == true;
    public static bool IsPremiumByAccount => IsPremiumUser && Account?.Subscription != null;
    public static bool IsPremiumByCode => IsPremiumUser && !IsPremiumByAccount;
    public static bool CanImportAccessCode => State.VpnProfile?.CanImportAccessCode == true;
    public static bool CanViewAccessCode => State.VpnProfile?.CanViewAccessCode == true;
    public static bool CanTryPremium => State.VpnProfile?.CanTryPremium == true;
    public static bool CanGoPremium => State.VpnProfile?.CanGoPremium == true;
    public static Guid? VpnProfileId => State.VpnProfile?.VpnProfileId ?? UserSettings.VpnProfileId;

    public static bool IsPremiumFeature(AppFeature feature)
    {
        return Descriptor.Premium?.Features.Contains(feature) ?? false;
    }

    // VpnHoodApp.IsPremiumFeatureAllowed, from what the API gives: a build with no premium tier
    // allows everything, a feature the tier does not sell is everyone's, the rest is the profile's.
    public static bool IsPremiumFeatureAllowed(AppFeature feature)
    {
        if (Descriptor.Premium == null)
            return true;

        if (!Descriptor.Premium.Features.Contains(feature))
            return true;

        return IsPremiumUser;
    }

    // the crown marks what this session does not have yet: a feature a tier sells, a location's
    // premium plan
    public static bool ShowCrown(bool isPremium)
    {
        return isPremium && !IsPremiumUser;
    }

    public static bool ShowCrown(AppFeature feature)
    {
        return ShowCrown(IsPremiumFeature(feature));
    }

    public static bool IsPrivateDnsActive(AppState state) => state.SystemPrivateDns?.IsActive == true;
    public static bool IsPrivateDnsCustomized(AppState state) => !string.IsNullOrEmpty(state.SystemPrivateDns?.Provider);

    public static bool IsDnsCustomized(AppState state)
    {
        if (IsPrivateDnsCustomized(state))
            return true;
        return UserSettings.DnsMode == DnsMode.AdapterDns && IsPremiumFeatureAllowed(AppFeature.CustomDns);
    }

    public static bool IsCustomEndpointActive(AppState state)
    {
        var profile = state.VpnProfile;
        return profile is { IsCustomServerEndpointsEnabled: true, CustomServerEndpoints.Length: > 0 };
    }

    public static bool IsNotificationEnabled(AppState state) => state.IsNotificationEnabled == true;

    public static ChannelProtocol ActiveProtocol(AppState state)
    {
        return IsConnected(state) ? state.ChannelProtocol : UserSettings.ChannelProtocol;
    }

    // The TCP forwarding switch as the user sees it: the setting, or what the server or Split Domains force; never the
    // session's status, which a save does not wait for: read back at once, the switch would jump to the old value
    // and stay there
    public static bool IsTcpForwardingSwitchOn(AppState state)
    {
        return state.TcpForwardingLockReason switch {
            TcpForwardingLockReason.ServerRequiredOn or TcpForwardingLockReason.SplitDomainRequiredOn => true,
            TcpForwardingLockReason.ServerRequiredOff => false,
            _ => UserSettings.UseTcpForwarding
        };
    }

    public static bool IsProtocolEnabled(AppState state, ChannelProtocol protocol)
    {
        return state.SessionInfo != null
            ? state.SessionInfo.ChannelProtocols.Contains(protocol)
            : IsShowProtocol(protocol);
    }

    public static bool IsShowProtocol(ChannelProtocol protocol)
    {
        return Descriptor.ChannelProtocols.Contains(protocol);
    }

    // this head has no web analytics of its own, so the tracker the app reports is the whole answer
    public static bool IsAnonymousTrackerSupported => Descriptor.IsAnonymousTrackerSupported;

    public static bool IsLocalNetworkAvailable(AppState state)
    {
        return !IsConnected(state) || state.SessionInfo?.IsLocalNetworkAllowed == true;
    }

    public static bool IsLocationAutoSelected(string? serverLocation)
    {
        return serverLocation is "*" or "*/*";
    }

    // VpnHoodApp.HasDebugCommand, from the settings: the commands are the words of DebugData1.
    public static bool HasDebugCommand(string command)
    {
        return UserSettings.DebugData1?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(command, StringComparer.OrdinalIgnoreCase) == true;
    }

    // Android only: the tools drive the Starlink app, which exists on no other platform; a debug
    // build keeps the page reachable while it is developed
    public static bool IsStarlinkToolsEnabled =>
        HasDebugCommand("/starlink") && (Descriptor.IsDebugMode || Descriptor.OsType == AppOsType.Android);

    // the store's method, never the portal's password (VpnHoodApp.primaryProviderId)
    public static string? PrimaryProviderId => Descriptor.AuthProviderIds.FirstOrDefault(x => x != "password");

    public static bool HasSignInChoice =>
        Descriptor.AuthProviderIds.Count > 1 || Descriptor.AuthProviderIds.FirstOrDefault() == "password";

    public static bool HasPasswordSignIn => Descriptor.AuthProviderIds.Contains("password");
}
