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

/// <summary>
/// Provides what the UI's pages read off the app, and the one way they reach it: the app's API, in process or over HTTP
/// from a paired browser.
/// </summary>
/// <remarks>
/// The pages read plain values held here, the features, the state, the settings and the profiles, never the app itself,
/// so the same pages run on the device and in a browser. The state is read again every second by the home's clock and
/// after every action; the rest when the state says the configuration moved or a page saved; the account when the state
/// says it changed.
/// </remarks>
public static class VhApp
{
    private static VpnHoodApi? _api;
    private static AppInfo? _info;
    private static AppState? _state;
    private static IReadOnlyList<string> _availableCultures = [];
    private static string? _sentCultureName;

    /// <summary>Gets the app's API.</summary>
    /// <exception cref="InvalidOperationException">The UI has not been given the app's API (<see cref="Init"/>).</exception>
    public static VpnHoodApi Api => _api ?? throw new InvalidOperationException(
        $"The UI has not been given the app's API. A head must call {nameof(VhApp)}.{nameof(Init)} before the UI starts.");

    /// <summary>Gets a value indicating whether the UI has the app's API.</summary>
    public static bool IsInit => _api != null;

    /// <summary>Gets a value indicating whether the head has configured the UI (<see cref="Configure"/>).</summary>
    public static bool IsConfigured { get; private set; }

    /// <summary>
    /// Gives the UI the app's API, and reads the app's information through it: the head's first step, before Avalonia
    /// starts, as the features read decide the theme.
    /// </summary>
    /// <param name="api">The app's API.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>In process the read completes at once.</remarks>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or, over HTTP, a request timed out.
    /// </exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Over HTTP, the app could not be reached.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, answered with an error it does
    /// not name, or answered with a body that cannot be read.
    /// </exception>
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

    /// <summary>
    /// Tells the app which languages the UI has words for, and gives it the UI's words for its own text: the head's second
    /// step, once the UI can say which languages it has.
    /// </summary>
    /// <param name="availableCultures">The languages the UI has words for.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>From then on, the app gets the UI's words again whenever the UI's language changes.</remarks>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or, over HTTP, a request timed out.
    /// </exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Over HTTP, the app could not be reached.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, answered with an error it does
    /// not name, or answered with a body that cannot be read.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The UI has not been given the app's API (<see cref="Init"/>), or the words have not been loaded
    /// (<see cref="Strings.InitAsync"/>).
    /// </exception>
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

    /// <summary>Gets the app's descriptor, as last read.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static AppDescriptor Descriptor => Info.Descriptor;

    /// <summary>Gets what the UI may ask of the device, as last read.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static DeviceIntentFeatures Intents => Info.IntentFeatures;

    /// <summary>Gets the person's settings, as last read.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static UserSettings UserSettings => Info.UserSettings;

    /// <summary>Gets the VPN profiles, as last read.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static IReadOnlyList<VpnProfileInfo> VpnProfileInfos => Info.VpnProfileInfos;

    /// <summary>Gets the languages the app offers, as last read.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static IReadOnlyList<UiCultureInfo> AvailableCultureInfos => Info.AvailableCultureInfos;

    /// <summary>Gets the app's state, as last read.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static AppState State => _state ?? Info.State;

    /// <summary>Gets a value indicating whether this UI is the remote: served by the app to a browser on another device.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool IsRemote => Info.IsRemote;

    /// <summary>Gets the VPN profile the app is set to, whole, from the last configuration read, or <see langword="null"/>.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static VpnProfileInfo? CurrentVpnProfileInfo => FindVpnProfileInfo(UserSettings.VpnProfileId);

    /// <summary>Finds a VPN profile in the last configuration read.</summary>
    /// <param name="vpnProfileId">The profile's ID, or <see langword="null"/>.</param>
    /// <returns>The profile, or <see langword="null"/> where there is none.</returns>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static VpnProfileInfo? FindVpnProfileInfo(Guid? vpnProfileId)
    {
        return vpnProfileId == null
            ? null
            : VpnProfileInfos.FirstOrDefault(x => x.VpnProfileId == vpnProfileId);
    }

    /// <summary>
    /// Reads the app's state again; where the configuration moved, the information too; and where the account changed, the
    /// account.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>A failed account read is logged and tried again after a pause; the state still shows.</remarks>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or, over HTTP, a request timed out.
    /// </exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Over HTTP, the app could not be reached.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, answered with an error it does
    /// not name, or answered with a body that cannot be read.
    /// </exception>
    /// <exception cref="InvalidOperationException">The UI has not been given the app's API (<see cref="Init"/>).</exception>
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

    /// <summary>Reads the app's information again, with its state.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or, over HTTP, a request timed out.
    /// </exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Over HTTP, the app could not be reached.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, answered with an error it does
    /// not name, or answered with a body that cannot be read.
    /// </exception>
    /// <exception cref="InvalidOperationException">The UI has not been given the app's API (<see cref="Init"/>).</exception>
    public static async Task ReloadInfo(CancellationToken cancellationToken)
    {
        _info = await Api.App.GetInfo(cancellationToken);
        _state = _info.State;
    }

    /// <summary>
    /// Saves the person's settings, and reads the information again, as the app applies them and may correct a value.
    /// </summary>
    /// <param name="userSettings">The settings: the object read here and changed.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or, over HTTP, a request timed out.
    /// </exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Over HTTP, the app could not be reached.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, answered with an error it does
    /// not name, or answered with a body that cannot be read.
    /// </exception>
    /// <exception cref="InvalidOperationException">The UI has not been given the app's API (<see cref="Init"/>).</exception>
    public static async Task SaveUserSettings(UserSettings userSettings, CancellationToken cancellationToken)
    {
        await Api.App.SetUserSettings(userSettings, cancellationToken);
        await ReloadInfo(cancellationToken);
    }

    /// <summary>Gets the signed-in person's account, as last read, or <see langword="null"/>.</summary>
    /// <remarks>
    /// It is read at start, after this UI's sign-in, sign-out or purchase, and when the state says it changed; never polled.
    /// </remarks>
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

    /// <summary>Reads the account, after a refresh from the account backend where asked.</summary>
    /// <param name="withRefresh">
    /// <see langword="true"/> to refresh it from the account backend first; a failed refresh is ignored, and the read that
    /// follows may still fail.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>An older read that answers after a newer one is not shown.</remarks>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or, over HTTP, a request timed out.
    /// </exception>
    /// <exception cref="System.Net.Http.HttpRequestException">Over HTTP, the app could not be reached.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// Over HTTP, the app failed with an exception of a type the transport does not rebuild, answered with an error it does
    /// not name, or answered with a body that cannot be read.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// A premium code waits to be uploaded, and the account backend keeps no codes on the account.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The app's information has not been read (<see cref="Init"/>), or the account's read found no current VPN profile, or
    /// several and none selected.
    /// </exception>
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

    /// <summary>Gets a value indicating whether the UI takes the TV layout: the device is a TV, and this UI is not the remote.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool IsTvUi => Descriptor.IsTv && !IsRemote;

    /// <summary>Gets a value indicating whether this UI is a browser driving a TV, so a wait for what the TV's own screen asks says to go on there.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool IsTvRemote => Descriptor.IsTv && IsRemote;

    /// <summary>
    /// Gets a value indicating whether the app takes no access keys, so its profiles are the ones it was built with.
    /// </summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool IsSingleProfileMode => !Descriptor.IsAddAccessKeySupported;

    /// <summary>Gets whether a state is connected: connected or unstable.</summary>
    /// <param name="state">The app's state.</param>
    /// <returns><see langword="true"/> where it is.</returns>
    public static bool IsConnected(AppState state)
    {
        return state.ConnectionState is AppConnectionState.Connected or AppConnectionState.Unstable;
    }

    /// <summary>Gets whether the last state read is connected: connected or unstable.</summary>
    /// <returns><see langword="true"/> where it is.</returns>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool IsConnected() => IsConnected(State);

    /// <summary>Gets a value indicating whether the build has a premium tier; without one, it is the full app.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool IsPremiumSupported => Descriptor.Premium != null;

    /// <summary>Gets a value indicating whether the current VPN profile is premium.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool IsPremiumUser => State.VpnProfile?.IsPremium == true;

    /// <summary>Gets a value indicating whether the premium comes from the account's subscription.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool IsPremiumByAccount => IsPremiumUser && Account?.Subscription != null;
    internal static bool IsPremiumByCode => IsPremiumUser && !IsPremiumByAccount;

    /// <summary>Gets a value indicating whether a premium code can be typed for the current VPN profile.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool CanImportAccessCode => State.VpnProfile?.CanImportAccessCode == true;

    /// <summary>Gets a value indicating whether the current VPN profile's premium code may be shown.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool CanViewAccessCode => State.VpnProfile?.CanViewAccessCode == true;

    /// <summary>Gets a value indicating whether the current VPN profile offers a premium trial.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool CanTryPremium => State.VpnProfile?.CanTryPremium == true;

    /// <summary>Gets a value indicating whether premium can be bought or had by a code for the current VPN profile.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool CanGoPremium => State.VpnProfile?.CanGoPremium == true;

    /// <summary>Gets the current VPN profile's ID: the state's, else the settings'.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static Guid? VpnProfileId => State.VpnProfile?.VpnProfileId ?? UserSettings.VpnProfileId;

    /// <summary>Gets whether the build's premium tier sells a feature.</summary>
    /// <param name="feature">The feature.</param>
    /// <returns><see langword="true"/> where it does.</returns>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool IsPremiumFeature(AppFeature feature)
    {
        return Descriptor.Premium?.Features.Contains(feature) ?? false;
    }

    /// <summary>
    /// Gets whether a feature is allowed: always without a premium tier or for a feature the tier does not sell, else for a
    /// premium profile.
    /// </summary>
    /// <param name="feature">The feature.</param>
    /// <returns><see langword="true"/> where it is.</returns>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool IsPremiumFeatureAllowed(AppFeature feature)
    {
        if (Descriptor.Premium == null)
            return true;

        if (!Descriptor.Premium.Features.Contains(feature))
            return true;

        return IsPremiumUser;
    }

    /// <summary>Gets whether to show the crown on something: where it is premium, and the profile is not.</summary>
    /// <param name="isPremium"><see langword="true"/> where the thing is premium.</param>
    /// <returns><see langword="true"/> to show the crown.</returns>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool ShowCrown(bool isPremium)
    {
        return isPremium && !IsPremiumUser;
    }

    /// <summary>Gets whether to show the crown on a feature: where the tier sells it, and the profile is not premium.</summary>
    /// <param name="feature">The feature.</param>
    /// <returns><see langword="true"/> to show the crown.</returns>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool ShowCrown(AppFeature feature)
    {
        return ShowCrown(IsPremiumFeature(feature));
    }

    /// <summary>Gets whether the device's private DNS is active.</summary>
    /// <param name="state">The app's state.</param>
    /// <returns><see langword="true"/> where it is.</returns>
    public static bool IsPrivateDnsActive(AppState state) => state.SystemPrivateDns?.IsActive == true;

    /// <summary>Gets whether the device's private DNS names a provider of its own.</summary>
    /// <param name="state">The app's state.</param>
    /// <returns><see langword="true"/> where it does.</returns>
    public static bool IsPrivateDnsCustomized(AppState state) => !string.IsNullOrEmpty(state.SystemPrivateDns?.Provider);

    /// <summary>
    /// Gets whether the DNS is customized: the device's private DNS names a provider, or the settings use the adapter's DNS
    /// where custom DNS is allowed.
    /// </summary>
    /// <param name="state">The app's state.</param>
    /// <returns><see langword="true"/> where it is.</returns>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool IsDnsCustomized(AppState state)
    {
        if (IsPrivateDnsCustomized(state))
            return true;
        return UserSettings.DnsMode == DnsMode.AdapterDns && IsPremiumFeatureAllowed(AppFeature.CustomDns);
    }

    /// <summary>Gets whether the current VPN profile connects to server addresses the person gave.</summary>
    /// <param name="state">The app's state.</param>
    /// <returns><see langword="true"/> where it does.</returns>
    public static bool IsCustomEndpointActive(AppState state)
    {
        var profile = state.VpnProfile;
        return profile is { IsCustomServerEndpointsEnabled: true, CustomServerEndpoints.Length: > 0 };
    }

    /// <summary>Gets whether the app's notifications are enabled.</summary>
    /// <param name="state">The app's state.</param>
    /// <returns><see langword="true"/> where they are.</returns>
    public static bool IsNotificationEnabled(AppState state) => state.IsNotificationEnabled == true;

    /// <summary>Gets the protocol in use: the session's while connected, else the settings'.</summary>
    /// <param name="state">The app's state.</param>
    /// <returns>The protocol.</returns>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static ChannelProtocol ActiveProtocol(AppState state)
    {
        return IsConnected(state) ? state.ChannelProtocol : UserSettings.ChannelProtocol;
    }

    /// <summary>
    /// Gets the TCP forwarding switch as the person sees it: on or off where the server or the split by domain forces it,
    /// else the setting.
    /// </summary>
    /// <param name="state">The app's state.</param>
    /// <returns><see langword="true"/> where the switch is on.</returns>
    /// <remarks>It is never the session's status, which a save does not wait for.</remarks>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool IsTcpForwardingSwitchOn(AppState state)
    {
        return state.TcpForwardingLockReason switch {
            TcpForwardingLockReason.ServerRequiredOn or TcpForwardingLockReason.SplitDomainRequiredOn => true,
            TcpForwardingLockReason.ServerRequiredOff => false,
            _ => UserSettings.UseTcpForwarding
        };
    }

    /// <summary>Gets whether a protocol can be chosen: the session's protocols while there is a session, else the build's.</summary>
    /// <param name="state">The app's state.</param>
    /// <param name="protocol">The protocol.</param>
    /// <returns><see langword="true"/> where it can.</returns>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool IsProtocolEnabled(AppState state, ChannelProtocol protocol)
    {
        return state.SessionInfo != null
            ? state.SessionInfo.ChannelProtocols.Contains(protocol)
            : IsShowProtocol(protocol);
    }

    /// <summary>Gets whether the build offers a protocol.</summary>
    /// <param name="protocol">The protocol.</param>
    /// <returns><see langword="true"/> where it does.</returns>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool IsShowProtocol(ChannelProtocol protocol)
    {
        return Descriptor.ChannelProtocols.Contains(protocol);
    }

    /// <summary>
    /// Gets a value indicating whether the app supports reporting anonymous usage: the whole answer, as this head has no web
    /// analytics of its own.
    /// </summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool IsAnonymousTrackerSupported => Descriptor.IsAnonymousTrackerSupported;

    /// <summary>Gets whether the local network can be reached: while disconnected, or where the session allows it.</summary>
    /// <param name="state">The app's state.</param>
    /// <returns><see langword="true"/> where it can.</returns>
    public static bool IsLocalNetworkAvailable(AppState state)
    {
        return !IsConnected(state) || state.SessionInfo?.IsLocalNetworkAllowed == true;
    }

    /// <summary>Gets whether a server location is the automatic one, <c>*</c> or <c>*/*</c>.</summary>
    /// <param name="serverLocation">The server location, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> where it is.</returns>
    public static bool IsLocationAutoSelected(string? serverLocation)
    {
        return serverLocation is "*" or "*/*";
    }

    // VpnHoodApp.HasDebugCommand, from the settings: the commands are the words of DebugData1.
    internal static bool HasDebugCommand(string command)
    {
        return UserSettings.DebugData1?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(command, StringComparer.OrdinalIgnoreCase) == true;
    }

    /// <summary>Gets a value indicating whether the Starlink tools are on: the debug command <c>/starlink</c> is set, on Android or in a debug build.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool IsStarlinkToolsEnabled =>
        HasDebugCommand("/starlink") && (Descriptor.IsDebugMode || Descriptor.OsType == AppOsType.Android);

    /// <summary>Gets the first sign-in method that is not the password's, or <see langword="null"/>.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static string? PrimaryProviderId => Descriptor.AuthProviderIds.FirstOrDefault(x => x != "password");

    /// <summary>Gets a value indicating whether the person chooses how to sign in: several methods, or the password's first.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool HasSignInChoice =>
        Descriptor.AuthProviderIds.Count > 1 || Descriptor.AuthProviderIds.FirstOrDefault() == "password";

    /// <summary>Gets a value indicating whether the password sign-in is offered.</summary>
    /// <exception cref="InvalidOperationException">The app's information has not been read (<see cref="Init"/>).</exception>
    public static bool HasPasswordSignIn => Descriptor.AuthProviderIds.Contains("password");
}
