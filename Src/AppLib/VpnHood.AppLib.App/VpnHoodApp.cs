using Microsoft.Extensions.Logging;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Diagnosing;
using VpnHood.AppLib.DtoConverters;
using VpnHood.AppLib.Exceptions;
using VpnHood.AppLib.Providers;
using VpnHood.AppLib.Services.Accounts;
using VpnHood.AppLib.Services.Ads;
using VpnHood.AppLib.Settings;
using VpnHood.AppLib.Utils;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Core.Client.VpnServices.Manager;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.IpLocations;
using VpnHood.Core.Common.IpLocations.Providers.Offlines;
using VpnHood.Core.Common.IpLocations.Providers.Onlines;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using PremiumOnlyException = VpnHood.AppLib.Exceptions.PremiumOnlyException;
using TaskExtensions = VpnHood.Core.Toolkit.Utils.TaskExtensions;

namespace VpnHood.AppLib;

public class VpnHoodApp : Singleton<VpnHoodApp>,
    IDisposable, IAsyncDisposable, IRegionProvider
{
    private const string FileNameLog = "app.log";
    private const string FileNamePersistState = "state.json";
    private const string FolderNameProfiles = "profiles";
    private readonly bool _useInternalLocationService;
    private readonly bool _useExternalLocationService;
    private readonly TimeSpan _locationServiceTimeout;
    private readonly bool _disconnectOnDispose;
    private readonly bool _autoDiagnose;
    private readonly bool _allowEndPointTracker;
    private readonly bool _allowRecommendUserReviewByServer;
    private readonly string? _ga4MeasurementId;
    private readonly TimeSpan _versionCheckInterval;
    private readonly TimeSpan _reconnectTimeout;
    private readonly TimeSpan _autoWaitTimeout;
    private readonly TimeSpan _serverQueryTimeout;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _sessionTimeout;
    private readonly TimeSpan _tcpTimeout;

    private readonly LogServiceOptions _logServiceOptions;
    private readonly AppPersistState _appPersistState;
    private readonly VpnServiceManager _vpnServiceManager;
    private readonly LocalIpRangeLocationProvider? _ipRangeLocationProvider;
    private readonly ITrackerFactory _trackerFactory;
    private readonly IDevice _device;
    private bool _isDisconnecting;
    private bool _isLoadingCountryIpRange;
    private bool _isFindingCountryCode;
    private AppConnectionState? _lastConnectionState;
    private CancellationTokenSource _connectCts = new();
    private CancellationTokenSource _showAdCts = new();
    private CancellationTokenSource _connectTimeoutCts = new();
    private VersionCheckResult? _versionCheckResult;
    private CultureInfo? _systemUiCulture;
    private UserSettings _oldUserSettings;
    private bool _isConnecting;
    private readonly Job _versionCheckJob;
    private readonly LogService _logService;
    private bool _isUserReviewRecommended;

    private ConnectionInfo ConnectionInfo => _vpnServiceManager.ConnectionInfo;
    private string VersionCheckFilePath => Path.Combine(StorageFolderPath, "version.json");
    public string TempFolderPath => Path.Combine(StorageFolderPath, "Temp");
    public event EventHandler? ConnectionStateChanged;
    public event EventHandler? UiHasChanged;
    public bool IsIdle => ConnectionState.IsIdle();
    public string StorageFolderPath { get; }
    public AppSettings Settings => SettingsService.AppSettings;
    public UserSettings UserSettings => SettingsService.AppSettings.UserSettings;
    public AppFeatures Features { get; }
    public ClientProfileService ClientProfileService { get; }
    public Diagnoser Diagnoser { get; } = new();
    public AppResources Resources { get; }
    public AppServices Services { get; }
    public AppSettingsService SettingsService { get; }
    public DeviceAppInfo[] InstalledApps => _device.InstalledApps;
    public LocalIpRangeLocationProvider IpRangeLocationProvider =>
        _ipRangeLocationProvider ?? throw new NotSupportedException("IpRangeLocationProvider is not supported.");
    public AppAdManager AdManager { get; }

    private VpnHoodApp(IDevice device, AppSettingsService settingsService, LogService logService, AppOptions options)
    {
        Resources = options.Resources;
        StorageFolderPath = options.StorageFolderPath ?? throw new ArgumentNullException(nameof(options.StorageFolderPath));
        SettingsService = settingsService;
        SettingsService.BeforeSave += SettingsBeforeSave;
        _device = device;
        _oldUserSettings = JsonUtils.JsonClone(UserSettings);
        _appPersistState = AppPersistState.Load(Path.Combine(StorageFolderPath, FileNamePersistState));
        _useInternalLocationService = options.UseInternalLocationService;
        _useExternalLocationService = options.UseExternalLocationService;
        _locationServiceTimeout = options.LocationServiceTimeout;
        _ga4MeasurementId = options.Ga4MeasurementId;
        _versionCheckInterval = options.VersionCheckInterval;
        _reconnectTimeout = options.ReconnectTimeout;
        _autoWaitTimeout = options.AutoWaitTimeout;
        _tcpTimeout = options.TcpTimeout;
        _versionCheckResult = JsonUtils.TryDeserializeFile<VersionCheckResult>(VersionCheckFilePath);
        _autoDiagnose = options.AutoDiagnose;
        _serverQueryTimeout = options.ServerQueryTimeout;
        _connectTimeout = options.ConnectTimeout;
        _allowEndPointTracker = options.AllowEndPointTracker;
        _disconnectOnDispose = options.DisconnectOnDispose;
        _logServiceOptions = options.LogServiceOptions;
        _logService = logService;
        _trackerFactory = options.TrackerFactory ?? new BuiltInTrackerFactory();
        _sessionTimeout = options.SessionTimeout;
        _allowRecommendUserReviewByServer = options.AllowRecommendUserReviewByServer;

        // IpRangeLocationProvider
        if (options.UseInternalLocationService) {
            if (options.Resources.IpLocationZipData == null) throw new ArgumentException("Internal location service needs IpLocationZipData.");
            _ipRangeLocationProvider = new LocalIpRangeLocationProvider(
                () => new ZipArchive(new MemoryStream(options.Resources.IpLocationZipData)),
                () => GetClientCountryCode(false));
        }

        ClientProfileService = new ClientProfileService(Path.Combine(StorageFolderPath, FolderNameProfiles));
        Diagnoser.StateChanged += (_, _) => FireConnectionStateChanged();

        // add a default test public server if not added yet
        var builtInProfileIds = ClientProfileService.ImportBuiltInAccessKeys(options.AccessKeys);

        // remove default client profile if not exists
        if (UserSettings.ClientProfileId != null &&
            ClientProfileService.FindById(UserSettings.ClientProfileId.Value) == null)
            UserSettings.ClientProfileId = null;

        // set first built in profile as default if default is not set
        UserSettings.ClientProfileId ??= builtInProfileIds.FirstOrDefault()?.ClientProfileId;

        // set the default server location if not set
        var uiProvider = options.UiProvider ?? new AppNotSupportedUiProvider();

        // initialize features
        Features = new AppFeatures {
            Version = typeof(VpnHoodApp).Assembly.GetName().Version ?? new Version(),
            IsExcludeAppsSupported = _device.IsExcludeAppsSupported,
            IsIncludeAppsSupported = _device.IsIncludeAppsSupported,
            IsAddAccessKeySupported = options.IsAddAccessKeySupported,
            IsPremiumFlagSupported = !options.IsAddAccessKeySupported,
            AutoRemoveExpiredPremium = options.AutoRemoveExpiredPremium,
            AllowEndPointStrategy = options.AllowEndPointStrategy,
            IsTv = device.IsTv,
            AdjustForSystemBars = options.AdjustForSystemBars,
            UpdateInfoUrl = options.UpdateInfoUrl != null ? new Uri(options.UpdateInfoUrl) : null,
            UiName = options.UiName,
            BuiltInClientProfileId = builtInProfileIds.FirstOrDefault()?.ClientProfileId,
            IsAccountSupported = options.AccountProvider != null,
            IsBillingSupported = options.AccountProvider?.BillingProvider != null,
            IsAlwaysOnSupported = uiProvider.IsAlwaysOnSupported,
            IsQuickLaunchSupported = uiProvider.IsQuickLaunchSupported,
            IsNotificationSupported = uiProvider.IsNotificationSupported,
            IsUserReviewSupported = options.UserReviewProvider != null,
            GaMeasurementId = options.Ga4MeasurementId,
            ClientId = CreateClientId(options.AppId, options.DeviceId ?? Settings.ClientId),
            AppId = options.AppId,
            DebugCommands = DebugCommands.All,
            IsLocalNetworkSupported = options.IsLocalNetworkSupported,
            IsDebugMode = options.IsDebugMode,
            CustomData = options.CustomData,
            PremiumFeatures = options.PremiumFeatures
        };

        // create tracker
        var tracker = options.TrackerFactory?.TryCreateTracker(new TrackerCreateParams {
            ClientId = Features.ClientId,
            ClientVersion = Features.Version,
            Ga4MeasurementId = Features.GaMeasurementId,
            UserAgent = null //not set yet
        });

        // initialize services
        Services = new AppServices {
            CultureProvider = options.CultureProvider ?? new AppCultureProvider(this),
            UserReviewProvider = options.UserReviewProvider,
            UpdaterProvider = options.UpdaterProvider,
            UiProvider = uiProvider,
            Tracker = tracker,
            AccountService = options.AccountProvider != null
                ? new AppAccountService(this, options.AccountProvider)
                : null,
        };

        // initialize client manager
        _vpnServiceManager = new VpnServiceManager(device, options.EventWatcherInterval);
        _vpnServiceManager.StateChanged += VpnService_StateChanged;

        // create ad service
        var adService = new AppAdService(
            regionProvider: this,
            adProviderItems: options.AdProviderItems,
            loadAdTimeout: options.AdOptions.LoadAdTimeout,
            loadAdPostDelay: options.AdOptions.LoadAdPostDelay,
            device: _device,
            tracker: tracker);

        AdManager = new AppAdManager(
            adService,
            _vpnServiceManager,
            extendByRewardedAdThreshold: options.AdOptions.ExtendByRewardedAdThreshold,
            showAdPostDelay: options.AdOptions.ShowAdPostDelay,
            isPreloadAdEnabled: options.AdOptions.PreloadAd);

        // Clear the last update status if a version has changed
        if (_versionCheckResult != null && _versionCheckResult.LocalVersion != Features.Version) {
            _versionCheckResult = null;
            File.Delete(VersionCheckFilePath);
        }

        // Apply settings but no error on startup
        ApplySettings();

        // schedule job
        AppUiContext.OnChanged += ActiveUiContext_OnChanged;

        // start the version check job
        _versionCheckJob = new Job(VersionCheckJob, new JobOptions {
            Name = "VersionCheck",
            Period = options.VersionCheckInterval,
            DueTime = options.VersionCheckInterval > TimeSpan.FromSeconds(5)
                ? TimeSpan.FromSeconds(2) // start immediately
                : options.VersionCheckInterval
        });

        // launch startup task
        Task.Run(OnStartup);
    }

    private async Task OnStartup()
    {
        // track ip location (try local provider, the server as satellite ip accepted if local failed)
        try {
            if (Services.Tracker != null && !SettingsService.AppSettings.IsStartupTrackerSent) {
                var countryCode = await GetClientCountryCodeAsync(allowVpnServer: false, CancellationToken.None);
                await Services.Tracker.Track(AppTrackerBuilder.BuildFirstLaunch(Features.ClientId, countryCode));
                SettingsService.AppSettings.IsStartupTrackerSent = true;
                SettingsService.AppSettings.Save();
            }
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not sent first launch tracker.");
        }
    }

    private void ApplySettings()
    {
        try {
            var state = State;
            var disconnectRequired = false;
            if (ConnectionInfo.IsStarted()) {
                var reconfigureParams = new ClientReconfigureParams {
                    UseTcpOverTun = HasDebugCommand(DebugCommands.UseTcpOverTun),
                    UseUdpChannel = UserSettings.UseUdpChannel,
                    DropUdp = HasDebugCommand(DebugCommands.DropUdp) || UserSettings.DropUdp,
                    DropQuic = UserSettings.DropQuic
                };
                // it is not important to take effect immediately
                _ = _vpnServiceManager.Reconfigure(reconfigureParams, CancellationToken.None);

                // check is disconnect required
                disconnectRequired =
                    UserSettings.UseVpnAdapterIpFilter != _oldUserSettings.UseVpnAdapterIpFilter ||
                    UserSettings.UseAppIpFilter != _oldUserSettings.UseAppIpFilter ||
                    UserSettings.TunnelClientCountry != _oldUserSettings.TunnelClientCountry ||
                    UserSettings.ClientProfileId != _oldUserSettings.ClientProfileId ||
                    UserSettings.IncludeLocalNetwork != _oldUserSettings.IncludeLocalNetwork ||
                    UserSettings.AppFiltersMode != _oldUserSettings.AppFiltersMode ||
                    !UserSettings.AppFilters.SequenceEqual(_oldUserSettings.AppFilters) ||
                    !VhUtils.SequenceEquals(UserSettings.DnsServers, _oldUserSettings.DnsServers);
            }

            // set default ContinueOnCapturedContext
            TaskExtensions.DefaultContinueOnCapturedContext = HasDebugCommand(DebugCommands.CaptureContext);

            // Update the profile country code if it is not set. Profile country policy always follows VpnServer location service
            ClientProfileService.ClientCountryCode = GetClientCountryCode(true);

            // Enable trackers
            if (Services.Tracker != null)
                Services.Tracker.IsEnabled = UserSettings.AllowAnonymousTracker;

            // sync culture to app settings
            Services.CultureProvider.SelectedCultures =
                UserSettings.CultureCode != null ? [UserSettings.CultureCode] : [];

            InitCulture();
            _oldUserSettings = JsonUtils.JsonClone(UserSettings);

            // disconnect
            if (state.CanDisconnect && disconnectRequired) {
                VhLogger.Instance.LogInformation("Disconnecting due to the settings change...");
                _ = TryDisconnect();
            }
        }
        catch (Exception ex) {
            ReportError(ex, "Could not apply settings.");
        }
    }

    private void ActiveUiContext_OnChanged(object? sender, EventArgs e)
    {
        var uiContext = AppUiContext.Context;
        if (IsIdle && AdManager.IsPreloadAdEnabled && uiContext != null)
            _ = VhUtils.TryInvokeAsync("PreloadAd", () => AdManager.AdService.LoadInterstitialAd(uiContext, CancellationToken.None));
    }

    public ClientProfileInfo? CurrentClientProfileInfo =>
        ClientProfileService.FindInfo(UserSettings.ClientProfileId ?? Guid.Empty);

    public ApiError? LastError {
        get {
            // don't show an error if it is not idle
            if (!IsIdle)
                return null;

            // Show error if a diagnosis has been requested and there is no error
            if (_appPersistState is { HasDiagnoseRequested: true } &&
                _appPersistState.LastError?.TypeName != nameof(UserCanceledException))
                return new NoErrorFoundException().ToApiError();

            // show last error
            if (_appPersistState.LastError != null)
                return _appPersistState.LastError;

            return ConnectionInfo.Error?.Equals(_appPersistState.LastClearedError) == true
                ? null : ConnectionInfo.Error;
        }
    }

    public AppState State {
        get {
            var clientProfileInfo = CurrentClientProfileInfo;
            var connectionInfo = ConnectionInfo;
            var connectionState = ConnectionState;
            var uiContext = AppUiContext.Context;
            var appState = new AppState {
                ConfigTime = Settings.ConfigTime,
                SessionStatus = connectionInfo.SessionStatus?.ToAppDto(AdManager.CanExtendByRewardedAd),
                SessionInfo = connectionInfo.SessionInfo?.ToAppDto(),
                ConnectionState = connectionState,
                CanConnect = connectionState.CanConnect(),
                CanDiagnose = connectionState.CanDiagnose(_appPersistState.HasDiagnoseRequested),
                CanDisconnect = connectionState.CanDisconnect(),
                IsUserReviewRecommended = _isUserReviewRecommended,
                IsIdle = IsIdle,
                PromptForLog = IsIdle && _appPersistState.HasDiagnoseRequested && _logService.Exists,
                LogExists = _logService.Exists,
                HasDiagnoseRequested = _appPersistState.HasDiagnoseRequested,
                ClientCountryCode = GetClientCountryCode(false), // split country don't follow server location
                ClientCountryName = VhUtils.TryGetCountryName(GetClientCountryCode(false)), // split country don't follow server location
                ConnectRequestTime = _appPersistState.ConnectRequestTime,
                CurrentUiCultureInfo = new UiCultureInfo(CultureInfo.DefaultThreadCurrentUICulture ?? SystemUiCulture),
                SystemUiCultureInfo = new UiCultureInfo(SystemUiCulture),
                VersionStatus = _versionCheckResult?.VersionStatus ?? VersionStatus.Unknown,
                PurchaseState = Services.AccountService?.BillingService?.PurchaseState,
                LastPublishInfo = _versionCheckResult?.GetNewerPublishInfo(),
                ClientProfile = clientProfileInfo?.ToBaseInfo(),
                LastError = LastError?.ToAppDto(),
                SystemBarsInfo = !Features.AdjustForSystemBars && uiContext != null
                    ? Services.UiProvider.GetSystemBarsInfo(uiContext)
                    : SystemBarsInfo.Default
            };

            return appState;
        }
    }

    public Task ForceUpdateState()
    {
        return _vpnServiceManager.ForceRefreshState(CancellationToken.None);
    }

    public AppConnectionState ConnectionState {
        get {
            var connectionInfo = ConnectionInfo;
            var clientState = connectionInfo.ClientState;

            // in diagnose mode, we need to either cancel it or wait for it
            if (Diagnoser.IsWorking)
                return AppConnectionState.Diagnosing;

            // let's service disconnect in the background and let the user connect again if it is disconnecting
            if (_isDisconnecting)
                return AppConnectionState.None;

            if (clientState == ClientState.Initializing || _isLoadingCountryIpRange || _isFindingCountryCode)
                return AppConnectionState.Initializing;

            if (clientState == ClientState.Waiting)
                return AppConnectionState.Waiting;

            if (clientState == ClientState.WaitingForAd)
                return AppConnectionState.WaitingForAd;

            if (clientState == ClientState.Connected)
                return AppConnectionState.Connected;

            if (clientState == ClientState.Connecting || _isConnecting)
                return AppConnectionState.Connecting;

            if (clientState == ClientState.Disconnecting)
                return AppConnectionState.None; // treat as none. let's service disconnect in the background

            return AppConnectionState.None;
        }
    }

    private void FireConnectionStateChanged()
    {
        // check changed state
        var connectionState = ConnectionState;
        if (connectionState == _lastConnectionState) return;
        _lastConnectionState = connectionState;
        Task.Run(() => ConnectionStateChanged?.Invoke(this, EventArgs.Empty));
    }

    public static VpnHoodApp Init(IDevice device, AppOptions options)
    {
        Directory.CreateDirectory(options.StorageFolderPath); //make sure the directory exists
        var settingsService = new AppSettingsService(options.StorageFolderPath);
        var logService = new LogService(Path.Combine(options.StorageFolderPath, FileNameLog));
        logService.Start(GetLogOptions(settingsService.AppSettings.UserSettings, options.LogServiceOptions, options.IsDebugMode),
            deleteOldReport: false);
        return new VpnHoodApp(device, settingsService, logService, options);
    }

    public void ClearLastError()
    {
        _appPersistState.LastError = null;
        _appPersistState.LastClearedError = ConnectionInfo.Error;
        _appPersistState.HasDiagnoseRequested = false;
        _isUserReviewRecommended = false;
    }

    private LogServiceOptions GetLogOptions()
    {
        return GetLogOptions(UserSettings, _logServiceOptions, Features.IsDebugMode);
    }

    private static LogServiceOptions GetLogOptions(UserSettings userSettings, LogServiceOptions appLogOptions, bool isDebug)
    {
        var logLevel = appLogOptions.MinLogLevel;
        if (HasDebugCommand(userSettings, DebugCommands.LogDebug) || isDebug) logLevel = LogLevel.Debug;
        if (HasDebugCommand(userSettings, DebugCommands.LogTrace)) logLevel = LogLevel.Trace;
        var logOptions = new LogServiceOptions {
            MinLogLevel = logLevel,
            LogAnonymous = !isDebug && (appLogOptions.LogAnonymous == true || userSettings.LogAnonymous),
            LogEventNames = LogService.GetLogEventNames(appLogOptions.LogEventNames, userSettings.DebugData1 ?? "").ToArray(),
            SingleLineConsole = appLogOptions.SingleLineConsole,
            LogToConsole = appLogOptions.LogToConsole,
            LogToFile = appLogOptions.LogToFile,
            AutoFlush = appLogOptions.AutoFlush,
            CategoryName = appLogOptions.CategoryName
        };
        return logOptions;
    }

    public async Task<bool> TryConnect(ConnectOptions? connectOptions = null,
        CancellationToken cancellationToken = default)
    {
        try {
            await Connect(connectOptions, cancellationToken);
            return true;
        }
        catch {
            return false; // don't throw exception, just return false
        }
    }

    public async Task Connect(ConnectOptions? connectOptions = null, CancellationToken cancellationToken = default)
    {
        try {
            connectOptions ??= new ConnectOptions();
            VhLogger.Instance.LogDebug(
                "Connection requested. ProfileId: {ProfileId}, ServerLocation: {ServerLocation}, Plan: {Plan}, Diagnose: {Diagnose}",
                connectOptions.ClientProfileId, connectOptions.ServerLocation, connectOptions.PlanId,
                connectOptions.Diagnose);

            // protect double call
            if (!IsIdle) {
                VhLogger.Instance.LogInformation("Disconnecting due to user request to connect...");
                await TryDisconnect().Vhc();
            }

            // Create a connect cancellation token
            _connectCts = new CancellationTokenSource();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _connectCts.Token);
            _isConnecting = true; //must be after checking IsIdle
            ClearLastError();
            await ConnectInternal1(connectOptions, linkedCts.Token);
        }
        catch (Exception ex) {
            ReportError(ex, "Could not establish the connection.");
            _appPersistState.LastError = ex.ToApiError();
            await TryDisconnect(); // await, to prevent VpnService_StateChanged clear the LastError
            throw;
        }
        finally {
            _isConnecting = false;
            FireConnectionStateChanged();
        }
    }


    private async Task ConnectInternal1(ConnectOptions connectOptions, CancellationToken cancellationToken)
    {
        // set use default clientProfile and serverLocation
        var clientProfileId = connectOptions.ClientProfileId ?? UserSettings.ClientProfileId ?? throw new NotExistsException("ClientProfile is not set.");
        var clientProfile = ClientProfileService.Get(clientProfileId);

        try {
            var clientProfileInfo = ClientProfileService.GetInfo(clientProfileId);
            var serverLocation =
                connectOptions.ServerLocation ?? clientProfileInfo.SelectedLocationInfo?.ServerLocation;

            // set timeout
            _connectTimeoutCts = new CancellationTokenSource(
                connectOptions.Diagnose ? _connectTimeout * 3 : _connectTimeout);

            // create cancellationToken after disconnecting previous connection
            using var linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _connectTimeoutCts.Token);

            // reset connection state
            _appPersistState.LastClearedError = null; // it is a new connection
            _appPersistState.HasDiagnoseRequested = connectOptions.Diagnose;
            _appPersistState.ConnectRequestTime = DateTime.Now;
            FireConnectionStateChanged();

            // initialize built-in tracker after acquire userAgent
            if (Services.Tracker == null && UserSettings.AllowAnonymousTracker)
                Services.Tracker = _trackerFactory.TryCreateTracker(new TrackerCreateParams {
                    ClientId = Features.ClientId,
                    ClientVersion = Features.Version,
                    Ga4MeasurementId = Features.GaMeasurementId,
                    UserAgent = connectOptions.UserAgent
                });

            //logOptions.
            VhLogger.Instance.LogDebug("Starting the log service...");
            _logService.Start(GetLogOptions());

            // set the current profile only if it has been updated to avoid unnecessary new config time
            if (clientProfile.ClientProfileId != UserSettings.ClientProfileId ||
                serverLocation != clientProfileInfo.SelectedLocationInfo?.ServerLocation) {
                clientProfile = ClientProfileService.Update(clientProfileId,
                    new ClientProfileUpdateParams { SelectedLocation = serverLocation });
                UserSettings.ClientProfileId = clientProfile.ClientProfileId;
                Settings.Save();
            }

            // log general info
            VhLogger.Instance.LogInformation("AppVersion: {AppVersion}, AppId: {Features.AppId}",
                GetType().Assembly.GetName().Version, Features.AppId);
            VhLogger.Instance.LogInformation("Time: {Time}", DateTime.UtcNow.ToString("u", new CultureInfo("en-US")));
            VhLogger.Instance.LogInformation("OS: {OsInfo}", _device.OsInfo);
            VhLogger.Instance.LogInformation("UserAgent: {userAgent}", connectOptions.UserAgent);
            VhLogger.Instance.LogInformation("UserSettings: {UserSettings}",
                JsonSerializer.Serialize(UserSettings, new JsonSerializerOptions { WriteIndented = true }));
            if (connectOptions.Diagnose) // log country name
                VhLogger.Instance.LogInformation("CountryCode: {CountryCode}",
                    VhUtils.TryGetCountryName(
                        await GetClientCountryCodeAsync(allowVpnServer: false, allowCache: true, linkedCts.Token)
                            .Vhc()));

            // request features for the first time
            VhLogger.Instance.LogDebug("Requesting Features ...");
            await RequestFeatures(linkedCts.Token).Vhc();

            // connect
            VhLogger.Instance.LogInformation("Client is Connecting ...");
            await ConnectInternal2(clientProfile.Token,
                    serverLocation: serverLocation,
                    userAgent: connectOptions.UserAgent,
                    planId: connectOptions.PlanId,
                    accessCode: clientProfile.AccessCode,
                    allowUpdateToken: true,
                    cancellationToken: linkedCts.Token)
                .Vhc();
        }
        catch (Exception) when (_appPersistState.LastError != null) {
            throw AppExceptionConverter.ApiErrorToException(_appPersistState.LastError) ?? _appPersistState.LastError.ToException();
        }
        catch (Exception) when (_connectCts.IsCancellationRequested) {
            throw new UserCanceledException("User has cancelled the connection.");
        }
        catch (Exception ex) {
            // check no internet connection, use the original cancellation token to avoid timeout exception
            if (_autoDiagnose && !_appPersistState.HasDiagnoseRequested &&
                ex is not UserCanceledException && ex is not SessionException) {
                VhLogger.Instance.LogDebug("Start checking client network...");

                // use the original cancellation token to avoid the timeout exception consumed by Connect
                await Diagnoser.CheckPureNetwork(cancellationToken).Vhc();
            }

            throw;
        }
    }

    private async Task ConnectInternal2(Token token, string? serverLocation, string? userAgent,
        ConnectPlanId planId, string? accessCode, bool allowUpdateToken, CancellationToken cancellationToken)
    {
        var profileInfo = CurrentClientProfileInfo ?? throw new NotExistsException("ClientProfile is not set.");

        try {
            // show token info
            VhLogger.Instance.LogInformation("TokenId: {TokenId}, SupportId: {SupportId}",
                VhLogger.FormatId(token.TokenId), VhLogger.FormatId(token.SupportId));

            // calculate vpnAdapterIpRanges
            var vpnAdapterIpRanges = IpNetwork.All.ToIpRanges();
            if (UserSettings.UseVpnAdapterIpFilter) {
                VerifyPremiumFeature(AppFeature.AdapterIpFilter);
                vpnAdapterIpRanges = vpnAdapterIpRanges.Intersect(
                    IpFilterParser.ParseIncludes(SettingsService.IpFilterSettings.AdapterIpFilterIncludes));
                vpnAdapterIpRanges = vpnAdapterIpRanges.Exclude(
                    IpFilterParser.ParseExcludes(SettingsService.IpFilterSettings.AdapterIpFilterExcludes));
            }

            // use default DNS servers if not premium account
            var dnsServers = UserSettings.DnsServers;
            if (!VhUtils.IsNullOrEmpty(dnsServers))
                VerifyPremiumFeature(AppFeature.CustomDns);

            // create clientOptions
            var clientOptions = new ClientOptions {
                AppName = Resources.Strings.AppName,
                ClientId = Features.ClientId,
                AccessKey = token.ToAccessKey(),
                SessionTimeout = _sessionTimeout,
                ReconnectTimeout = _reconnectTimeout,
                AutoWaitTimeout = _autoWaitTimeout,
                IncludeLocalNetwork = UserSettings.IncludeLocalNetwork && Features.IsLocalNetworkSupported,
                IncludeIpRanges = (await GetIncludeIpRanges(cancellationToken)).ToArray(),
                VpnAdapterIncludeIpRanges = vpnAdapterIpRanges.ToArray(),
                MaxPacketChannelCount = UserSettings.MaxPacketChannelCount,
                ConnectTimeout = _tcpTimeout,
                ServerQueryTimeout = _serverQueryTimeout,
                UseNullCapture = HasDebugCommand(DebugCommands.NullCapture),
                DropUdp = HasDebugCommand(DebugCommands.DropUdp) || UserSettings.DropUdp,
                DropQuic = UserSettings.DropQuic,
                ServerLocation = ServerLocationInfo.IsAutoLocation(serverLocation) ? null : serverLocation,
                PlanId = planId,
                AccessCode = accessCode,
                UseTcpOverTun = HasDebugCommand(DebugCommands.UseTcpOverTun),
                UseUdpChannel = UserSettings.UseUdpChannel,
                DomainFilter = UserSettings.DomainFilter,
                AllowAnonymousTracker = UserSettings.AllowAnonymousTracker,
                AllowEndPointTracker = UserSettings.AllowAnonymousTracker && _allowEndPointTracker,
                AllowTcpReuse = !HasDebugCommand(DebugCommands.NoTcpReuse),
                ExcludeApps = UserSettings.AppFiltersMode == FilterMode.Exclude ? UserSettings.AppFilters : null,
                IncludeApps = UserSettings.AppFiltersMode == FilterMode.Include ? UserSettings.AppFilters : null,
                DnsServers = dnsServers,
                LogServiceOptions = GetLogOptions(),
                Ga4MeasurementId = _ga4MeasurementId,
                Version = Features.Version,
                TrackerFactoryAssemblyQualifiedName = _trackerFactory.GetType().AssemblyQualifiedName,
                UserAgent = userAgent ?? ClientOptions.Default.UserAgent,
                EndPointStrategy = Features.AllowEndPointStrategy
                    ? UserSettings.EndPointStrategy
                    : EndPointStrategy.Auto,
                DebugData1 = UserSettings.DebugData1,
                DebugData2 = UserSettings.DebugData2,
                SessionName = profileInfo.ClientProfileName,
                CustomServerEndpoints = profileInfo.CustomServerEndpoints,
                AllowAlwaysOn = IsPremiumFeatureAllowed(AppFeature.AlwaysOn),
                UserReview = Settings.UserReview
            };

            VhLogger.Instance.LogDebug(
                "Launching VpnService ... DiagnoseMode: {DiagnoseMode}, AutoDiagnose: {AutoDiagnose}",
                _appPersistState.HasDiagnoseRequested, _autoDiagnose);

            // start to diagnose if requested
            if (_appPersistState.HasDiagnoseRequested) {
                var hostEndPoints = await EndPointResolver
                    .ResolveHostEndPoints(token.ServerToken, UserSettings.EndPointStrategy, cancellationToken).Vhc();
                await Diagnoser.CheckEndPoints(hostEndPoints, cancellationToken).Vhc();
                await Diagnoser.CheckPureNetwork(cancellationToken).Vhc();
                await _vpnServiceManager.Start(clientOptions, cancellationToken).Vhc();
                await Diagnoser.CheckVpnNetwork(cancellationToken).Vhc();
            }
            // start client
            else {
                await _vpnServiceManager.Start(clientOptions, cancellationToken).Vhc();
            }

            var connectionInfo = ConnectionInfo;
            if (connectionInfo.SessionInfo == null)
                throw new InvalidOperationException("Client is connected but there is no session.");

            // update the access token if AccessKey is set
            if (!string.IsNullOrWhiteSpace(connectionInfo.SessionInfo.AccessKey))
                ClientProfileService.TryUpdateTokenByAccessKey(token.TokenId, connectionInfo.SessionInfo.AccessKey);

            // update client country
            if (connectionInfo.SessionInfo.ClientCountry != null)
                UpdateClientIpLocationFromServer(connectionInfo.SessionInfo.ClientPublicIpAddress,
                    connectionInfo.SessionInfo.ClientCountry);
        }
        catch (OperationCanceledException ex) when (_connectTimeoutCts.IsCancellationRequested) {
            throw new ConnectionTimeoutException(
                $"Could not establish the connection in {_connectTimeout.TotalSeconds} seconds.", ex);
        }
        catch (AlwaysOnNotAllowedException) {
            throw PremiumOnlyException.Create(AppFeature.AlwaysOn);
        }
        catch (Exception ex) {
            if (ex is SessionException sessionException)
                ProcessSessionException(sessionException, profileInfo);

            // try to update the token from url after connection or error if ResponseAccessKey is not set
            if (ex is not NoInternetException && // diagnoser
                allowUpdateToken &&
                !VhUtils.IsNullOrEmpty(token.ServerToken.Urls) &&
                await ClientProfileService.UpdateServerTokenByUrls(token, cancellationToken).Vhc()) {
                // reconnect using the new token
                ReportError(ex, "Could not establish the connection. Reconnecting using the new token...");
                token = ClientProfileService.GetToken(token.TokenId);
                await ConnectInternal2(token,
                        serverLocation: serverLocation,
                        userAgent: userAgent,
                        planId: planId,
                        accessCode: accessCode,
                        allowUpdateToken: false,
                        cancellationToken: cancellationToken)
                    .Vhc();
                return;
            }

            throw;
        }
    }

    private void ProcessSessionException(SessionException sessionException, ClientProfileInfo profileInfo)
    {
        // update the access token if AccessKey is set
        if (!string.IsNullOrWhiteSpace(sessionException.SessionResponse.AccessKey)) {
            ClientProfileService.TryUpdateTokenByAccessKey(profileInfo.TokenId, sessionException.SessionResponse.AccessKey);
            sessionException.SessionResponse.AccessKey = null;
        }

        // update client country by server
        if (sessionException.SessionResponse is { ClientCountry: not null, ClientPublicAddress: not null })
            UpdateClientIpLocationFromServer(sessionException.SessionResponse.ClientPublicAddress, sessionException.SessionResponse.ClientCountry);

        // process session error codes
        switch (sessionException.SessionResponse.ErrorCode) {

            // reset the selected location if no server available or premium location required
            case SessionErrorCode.NoServerAvailable or SessionErrorCode.PremiumLocation:
                VhLogger.Instance.LogWarning("No server available or premium location required. Resetting selected location.");
                ClientProfileService.Update(profileInfo.ClientProfileId,
                    new ClientProfileUpdateParams { SelectedLocation = new Patch<string?>(null) });
                break;

            // remove the access code if it is rejected
            case SessionErrorCode.AccessCodeRejected when Features.AutoRemoveExpiredPremium:
                VhLogger.Instance.LogWarning("Access code rejected. Removing the access code from profile.");
                ClientProfileService.Update(profileInfo.ClientProfileId,
                    new ClientProfileUpdateParams { AccessCode = new Patch<string?>(null) });
                break;

            // remove the client profile if access expired
            case SessionErrorCode.AccessExpired when Features.AutoRemoveExpiredPremium && profileInfo.IsForAccount:
                VhLogger.Instance.LogWarning("Access expired. Removing the premium profile.");
                ClientProfileService.Delete(profileInfo.ClientProfileId);
                _ = Services.AccountService?.Refresh(true);
                break;
        }
    }

    public bool HasDebugCommand(string command)
    {
        return HasDebugCommand(UserSettings, command);
    }

    private static bool HasDebugCommand(UserSettings userSettings, string command)
    {
        if (string.IsNullOrEmpty(userSettings.DebugData1))
            return false;

        // check if the debug command is enabled
        return userSettings.DebugData1?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(command, StringComparer.OrdinalIgnoreCase) == true;
    }

    private void UpdateClientIpLocationFromServer(IPAddress publicIpAddress, string countryCode)
    {
        var clientIpLocationByServer = new IpLocation {
            CountryCode = countryCode,
            IpAddress = publicIpAddress,
            RegionName = null,
            CityName = null,
            CountryName = VhUtils.TryGetCountryName(countryCode) ?? "Unknown"
        };

        if (!JsonUtils.JsonEquals(clientIpLocationByServer, SettingsService.AppSettings.ClientIpLocationByServer)) {
            SettingsService.AppSettings.ClientIpLocationByServer = clientIpLocationByServer;
            SettingsService.AppSettings.Save();
        }
    }

    private async Task RequestFeatures(CancellationToken cancellationToken)
    {
        if (AppUiContext.Context?.IsActive != true)
            return;

        // QuickLaunch
        if (Services.UiProvider.IsQuickLaunchSupported &&
            IsPremiumFeatureAllowed(AppFeature.QuickLaunch) &&
            Settings.IsQuickLaunchEnabled is null &&
            _appPersistState.SuccessfulConnectionsCount > 3) {
            try {
                VhLogger.Instance.LogInformation("Prompting for Quick Launch...");
                Settings.IsQuickLaunchEnabled =
                    await Services.UiProvider
                        .RequestQuickLaunch(AppUiContext.RequiredContext, cancellationToken).Vhc();
            }
            catch (Exception ex) {
                ReportError(ex, "Could not add QuickLaunch.");
            }

            Settings.Save();
        }

        // Notification
        if (Services.UiProvider.IsNotificationSupported &&
            Settings.IsNotificationEnabled is null) {
            try {
                VhLogger.Instance.LogInformation("Prompting for notifications...");
                Settings.IsNotificationEnabled =
                    await Services.UiProvider
                        .RequestNotification(AppUiContext.RequiredContext, cancellationToken).Vhc();
            }
            catch (Exception ex) {
                ReportError(ex, "Could not enable Notification.");
            }

            Settings.Save();
        }
    }

    public CultureInfo SystemUiCulture =>
        _systemUiCulture ??
        new CultureInfo(Services.CultureProvider.SystemCultures.FirstOrDefault() ??
                        CultureInfo.InstalledUICulture.Name);

    private void InitCulture()
    {
        // find the first available culture that matches with the system culture
        _systemUiCulture = Services.CultureProvider.GetBestCultureInfo();

        // set default culture
        var firstSelected = Services.CultureProvider.SelectedCultures.FirstOrDefault();
        CultureInfo.CurrentUICulture = firstSelected != null ? new CultureInfo(firstSelected) : _systemUiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;

        // sync UserSettings from the System App Settings
        UserSettings.CultureCode = firstSelected;
    }

    private void SettingsBeforeSave(object? sender, EventArgs e)
    {
        ApplySettings();
    }

    public string GetClientCountryCode(bool allowVpnServer)
    {
        // try by server 
        if (allowVpnServer && SettingsService.AppSettings.ClientIpLocationByServer != null)
            return SettingsService.AppSettings.ClientIpLocationByServer.CountryCode;

        // try by client service providers
        return SettingsService.AppSettings.ClientIpLocation?.CountryCode
               ?? RegionInfo.CurrentRegion.Name;
    }

    public Task<string> GetClientCountryCodeAsync(bool allowVpnServer, CancellationToken cancellationToken)
    {
        return GetClientCountryCodeAsync(allowVpnServer: allowVpnServer, allowCache: true, cancellationToken);
    }

    public async Task<string> GetClientCountryCodeAsync(bool allowVpnServer, bool allowCache, CancellationToken cancellationToken)
    {
        var ipLocation = await GetClientIpLocation(allowVpnServer: allowVpnServer, allowCache: allowCache, cancellationToken);
        return ipLocation?.CountryCode ?? RegionInfo.CurrentRegion.Name;
    }

    private readonly AsyncLock _currentLocationLock = new();
    private async Task<IpLocation?> GetClientIpLocation(bool allowVpnServer, bool allowCache, CancellationToken cancellationToken)
    {
        using var scopeLock = await _currentLocationLock.LockAsync(cancellationToken);

        if (allowCache) {
            if (allowVpnServer && SettingsService.AppSettings.ClientIpLocationByServer?.CountryCode != null)
                return SettingsService.AppSettings.ClientIpLocationByServer;

            // try by client service providers
            if (SettingsService.AppSettings.ClientIpLocation != null)
                return SettingsService.AppSettings.ClientIpLocation;
        }

        // try to get the current ip location by local service
        var ipLocation = await TryGetCurrentIpLocationByLocal(cancellationToken).Vhc();
        if (ipLocation != null)
            return ipLocation;

        // try to use cache if it could not get by local service
        if (allowVpnServer && SettingsService.AppSettings.ClientIpLocationByServer?.CountryCode != null)
            return SettingsService.AppSettings.ClientIpLocationByServer;

        // try by client service providers
        return SettingsService.AppSettings.ClientIpLocation;
    }

    private async Task<IpLocation?> TryGetCurrentIpLocationByLocal(CancellationToken cancellationToken)
    {
        if (!_useExternalLocationService)
            return null;

        try {
            VhLogger.Instance.LogDebug("Getting Country from external location service...");

            _isFindingCountryCode = true;
            FireConnectionStateChanged();

            using var httpClient = new HttpClient();
            const string userAgent = "VpnHood-Client";
            var providers = new List<IIpLocationProvider> {
                    new CloudflareLocationProvider(httpClient, userAgent),
                    new IpLocationIoProvider(httpClient, userAgent, apiKey: null)
                };

            // InternalLocationService needs current ip from external service, so it is inside the if block
            if (_useInternalLocationService)
                providers.Add(IpRangeLocationProvider);

            var compositeProvider = new CompositeIpLocationProvider(VhLogger.Instance, providers,
                providerTimeout: _locationServiceTimeout);
            var ipLocation = await compositeProvider.GetCurrentLocation(cancellationToken).Vhc();
            SettingsService.AppSettings.ClientIpLocation = ipLocation;
            SettingsService.AppSettings.Save();
            return ipLocation;
        }
        catch (Exception ex) {
            ReportError(ex, "Could not find country code.");
            return null;
        }
        finally {
            _isFindingCountryCode = false;
            FireConnectionStateChanged();
        }
    }

    public void SetUserReview(int rate)
    {
        _isUserReviewRecommended = false;
        Settings.UserReview = new UserReview {
            AppVersion = Features.Version,
            Rate = rate,
            Time = DateTime.UtcNow
        };
    }

    private void VpnService_StateChanged(object? sender, EventArgs e)
    {
        // clear the last error when get out of idle state, because it indicates a new connection has started
        // do not call ClearLastError, it will clear diagnose request state. it must be called by the user
        if (!IsIdle && !_isConnecting) // don't clear if it is initiated by connect
            _appPersistState.LastError = null;

        // Show ad if it is waiting for ad
        if (ConnectionState is AppConnectionState.WaitingForAd)
            _ = TryShowAd();

        // check the version after the first connection
        if (ConnectionState is AppConnectionState.Connected)
            _ = VhUtils.TryInvokeAsync("VersionCheck", () => VersionCheck(delay: TimeSpan.Zero, force: false,
                cancellationToken: CancellationToken.None));

        // fire connection state changed
        FireConnectionStateChanged();
    }

    private readonly AsyncLock _showAdLock = new();
    private async Task TryShowAd()
    {
        // lock session
        using var scopeLock = await _showAdLock.LockAsync(timeout: TimeSpan.Zero);
        if (!scopeLock.Succeeded || AdManager.IsShowing)
            return; // already showing ad

        ArgumentNullException.ThrowIfNull(ConnectionInfo.SessionInfo);

        try {
            await _showAdCts.TryCancelAsync().Vhc();
            _showAdCts.Dispose();
            _showAdCts = new CancellationTokenSource();

            await AdManager.ShowAd(ConnectionInfo.SessionInfo.SessionId, ConnectionInfo.SessionInfo.AdRequirement, _showAdCts.Token);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not show ad.");
            if (!_showAdCts.IsCancellationRequested) {
                _appPersistState.LastError = ex.ToApiError();
                _ = TryDisconnect();
            }
        }
    }

    public async Task TryDisconnect()
    {
        try {
            await Disconnect().Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not disconnect gracefully.");
        }
    }

    private readonly AsyncLock _disconnectLock = new();
    public async Task Disconnect()
    {
        using var scopeLock = await _disconnectLock.LockAsync();
        if (_isDisconnecting)
            return; // already disconnecting

        try {
            // set _isDisconnecting
            VhLogger.Instance.LogInformation("Disconnect has been requested.");

            // store states before setting _isDisconnecting
            var state = State; 

            // save SuccessfulConnectionsCount
            if (FastDateTime.UtcNow > state.SessionInfo?.CreatedTime.AddMinutes(15) && // 15 minutes
                state.SessionStatus?.SessionTraffic.Total > 5_000_000) // 5MB
                _appPersistState.SuccessfulConnectionsCount++;

            // set review needed after disconnecting. It must be in connected state
            _isUserReviewRecommended =
                state.ConnectionState is AppConnectionState.Connected &&
                _appPersistState.LastError is null && 
                _allowRecommendUserReviewByServer &&
                Features.IsUserReviewSupported &&
                ConnectionInfo.SessionStatus?.IsUserReviewRecommended == true;

            // set after performing on disconnect task, because it will change the state
            _isDisconnecting = true;
            
            await _connectCts.TryCancelAsync().Vhc();
            _connectCts.Dispose();

            await _showAdCts.TryCancelAsync().Vhc();
            _showAdCts.Dispose();

            // stop VpnService if it is running
            await _vpnServiceManager.TryStop().Vhc();

        }
        finally {
            _isDisconnecting = false;
            FireConnectionStateChanged();
        }
    }

    public void VersionCheckPostpone()
    {
        // version status is unknown when the app container can do it
        _versionCheckResult = null;
        if (File.Exists(VersionCheckFilePath))
            File.Delete(VersionCheckFilePath);

        // set the latest ignored time
        _appPersistState.UpdateIgnoreTime = DateTime.Now;
    }

    private async ValueTask VersionCheckJob(CancellationToken cancellationToken)
    {
        await VersionCheck(force: false, delay: TimeSpan.Zero, cancellationToken: cancellationToken);
    }

    private readonly AsyncLock _versionCheckLock = new();
    public async Task VersionCheck(bool force, TimeSpan delay, CancellationToken cancellationToken)
    {
        using var lockAsync = await _versionCheckLock.LockAsync(cancellationToken).Vhc();
        if (!force && _appPersistState.UpdateIgnoreTime + _versionCheckInterval > DateTime.Now)
            return;

        // Wait for delay. Useful for waiting for an ad to send its tracker
        await Task.Delay(delay, cancellationToken).Vhc();

        // set timeout
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);


        // check the version by app container
        try {
            if (AppUiContext.Context != null && Services.UpdaterProvider != null &&
                await Services.UpdaterProvider.Update(AppUiContext.RequiredContext, linkedCts.Token).Vhc()) {
                VersionCheckPostpone();
                return;
            }
        }
        catch (Exception ex) {
            ReportWarning(ex, "Could not check version by VersionCheck.");
        }

        // check the version by UpdateInfoUrl
        _versionCheckResult = await VersionCheckByUpdateInfo(linkedCts.Token).Vhc();

        // save the result
        if (_versionCheckResult != null)
            await File.WriteAllTextAsync(VersionCheckFilePath,
                JsonSerializer.Serialize(_versionCheckResult), linkedCts.Token).Vhc();

        else if (File.Exists(VersionCheckFilePath))
            File.Delete(VersionCheckFilePath);
    }

    private async Task<VersionCheckResult?> VersionCheckByUpdateInfo(CancellationToken cancellationToken)
    {
        try {
            if (Features.UpdateInfoUrl == null)
                return null; // no update info url. Job done

            VhLogger.Instance.LogDebug("Retrieving the latest publish info...");

            using var httpClient = new HttpClient();
            var publishInfoJson = await httpClient.GetStringAsync(Features.UpdateInfoUrl, cancellationToken).Vhc();
            var latestPublishInfo = JsonUtils.Deserialize<PublishInfo>(publishInfoJson);
            VersionStatus versionStatus;

            // Check the version
            if (latestPublishInfo.Version == null)
                throw new Exception("Version is not available in publish info.");

            // set default notification delay
            if (Features.Version <= latestPublishInfo.DeprecatedVersion)
                versionStatus = VersionStatus.Deprecated;

            else if (Features.Version < latestPublishInfo.Version &&
                     DateTime.UtcNow - latestPublishInfo.ReleaseDate > latestPublishInfo.NotificationDelay)
                versionStatus = VersionStatus.Old;

            else
                versionStatus = VersionStatus.Latest;

            // save the result
            var checkResult = new VersionCheckResult {
                LocalVersion = Features.Version,
                VersionStatus = versionStatus,
                PublishInfo = latestPublishInfo,
                CheckedTime = DateTime.UtcNow
            };

            VhLogger.Instance.LogInformation(
                "The latest publish info has been retrieved. VersionStatus: {VersionStatus}, LatestVersion: {LatestVersion}",
                versionStatus, latestPublishInfo.Version);

            return checkResult; // Job done
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning("Could not retrieve the latest publish info information. Error: {Error}",
                ex.Message);
            return null; // could not retrieve the latest publish info. try later
        }
    }

    public async Task<IpRangeOrderedList> GetIncludeIpRanges(CancellationToken cancellationToken)
    {
        // calculate vpnAdapterIpRanges
        var ipRanges = IpNetwork.All.ToIpRanges();
        if (UserSettings.UseAppIpFilter) {
            VerifyPremiumFeature(AppFeature.AppIpFilter);
            ipRanges = ipRanges.Intersect(
                IpFilterParser.ParseIncludes(SettingsService.IpFilterSettings.AppIpFilterIncludes));

            ipRanges = ipRanges.Exclude(
                IpFilterParser.ParseExcludes(SettingsService.IpFilterSettings.AppIpFilterExcludes));
        }

        // exclude client country IPs
        if (UserSettings.TunnelClientCountry)
            return ipRanges;

        try {
            _isLoadingCountryIpRange = true;
            using var workScope = new AutoDispose(() => { _isLoadingCountryIpRange = false; FireConnectionStateChanged(); });
            FireConnectionStateChanged();

            if (!_useInternalLocationService)
                throw new InvalidOperationException("Could not use internal location service because it is disabled.");

            // do not use cache and server country code, maybe client on satellite, and they need to split their own country IPs 
            var countryCode = await GetClientCountryCodeAsync(allowVpnServer: false, allowCache: false, cancellationToken).Vhc();
            var countryIpRanges = await IpRangeLocationProvider.GetIpRanges(countryCode).Vhc();
            VhLogger.Instance.LogInformation("Client CountryCode is: {CountryCode}", VhUtils.TryGetCountryName(countryCode));
            ipRanges = ipRanges.Exclude(countryIpRanges);
        }
        catch (Exception ex) {
            ReportError(ex, "Could not get ip locations of your country.");
            if (!UserSettings.TunnelClientCountry) {
                UserSettings.TunnelClientCountry = true;
                Settings.Save();
            }
        }

        return ipRanges;
    }

    // make sure the active profile is valid and exist
    internal void ValidateAccountClientProfiles(bool updateCurrentClientProfile)
    {
        // Select the best client profile from their account.
        if (updateCurrentClientProfile) {
            var clientProfiles = ClientProfileService
                .List()
                .Where(x => x.IsForAccount)
                .ToArray();

            if (clientProfiles.Any()) {
                UserSettings.ClientProfileId = clientProfiles.Last().ClientProfileId;
                Settings.Save();
            }
        }

        // update the current profile if the current profile does not exist
        if (ClientProfileService.FindById(UserSettings.ClientProfileId ?? Guid.Empty) == null) {
            var clientProfiles = ClientProfileService.List();
            UserSettings.ClientProfileId = clientProfiles.Length == 1 ? clientProfiles.First().ClientProfileId : null;
            Settings.Save();
        }
    }

    public async Task CopyLogToStream(Stream destination)
    {
        await using var write = new StreamWriter(destination, Encoding.UTF8, bufferSize: -1, leaveOpen: true);

        // write app log
        try {
            if (File.Exists(_logService.LogFilePath)) {
                await write.WriteLineAsync("-----------------------");
                await write.WriteLineAsync("VPN App Log");
                await write.WriteLineAsync("-----------------------");
                await write.FlushAsync();

                await using var appLogStream = new FileStream(_logService.LogFilePath,
                    FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                await appLogStream.CopyToAsync(destination);
                await destination.FlushAsync();
            }
        }
        catch (Exception ex) {
            await write.WriteLineAsync($"Error: Could not read application log. {ex.Message}");
            await write.FlushAsync();
        }

        // write vpn service log
        try {
            if (File.Exists(_vpnServiceManager.LogFilePath)) {
                await write.WriteLineAsync("");
                await write.WriteLineAsync("-----------------------");
                await write.WriteLineAsync("VPN Service Log");
                await write.WriteLineAsync("-----------------------");
                await write.FlushAsync();

                await using var serviceLogStream = new FileStream(_vpnServiceManager.LogFilePath,
                    FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                await serviceLogStream.CopyToAsync(destination);
                await destination.FlushAsync();
            }
        }
        catch (Exception ex) {
            await write.WriteLineAsync($"Error: Could not read vpn service log. {ex.Message}");
            await write.FlushAsync();
        }
    }

    private void ReportError(Exception ex, string message, [CallerMemberName] string action = "n/a")
    {
        _ = Services.Tracker?.TryTrackError(ex, message, action);
        VhLogger.Instance.LogError(ex, message);
    }

    private void ReportWarning(Exception ex, string message, [CallerMemberName] string action = "n/a")
    {
        _ = Services.Tracker?.TryTrackWarningAsync(ex, message, action);
        VhLogger.Instance.LogWarning(ex, message);
    }

    public async Task<AppPurchaseOptions> GetPurchaseOptions()
    {
        var profileInfo = CurrentClientProfileInfo;
        var purchaseUrlMode = profileInfo?.PurchaseUrlMode;
        var purchaseUrl = profileInfo?.PurchaseUrl;

        // get subscription plans from the store
        var subscriptionPlans = Array.Empty<SubscriptionPlan>();
        string? storeName = null;
        ApiError? apiError = null;
        var isStoreAvailable = false;
        if (purchaseUrlMode != PurchaseUrlMode.HideStore) {
            try {
                var billingService = Services.AccountService?.BillingService;
                storeName = billingService?.ProviderName;
                if (billingService != null) {
                    subscriptionPlans = await billingService.GetSubscriptionPlans();
                    isStoreAvailable = true;
                }
            }
            catch (Exception ex) {
                apiError = ex.ToApiError();
            }
        }

        // calculate purchase url
        var externalUrl = purchaseUrlMode switch {
            PurchaseUrlMode.HideStore => purchaseUrl,
            PurchaseUrlMode.WithStore => purchaseUrl,
            PurchaseUrlMode.WhenNoStore when !isStoreAvailable => purchaseUrl,
            _ => null
        };

        var purchaseOptions = new AppPurchaseOptions {
            StoreName = storeName,
            SubscriptionPlans = subscriptionPlans,
            StoreError = apiError, // no error if purchaseUrl is set
            PurchaseUrl = externalUrl,
        };

        return purchaseOptions;
    }

    private static string CreateClientId(string appId, string deviceId)
    {
        // Convert the combined string to bytes
        var uid = $"{appId}:{deviceId}";
        var uiBytes = Encoding.UTF8.GetBytes(uid);

        // Create an MD5 instance and compute the hash
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(uiBytes);

        // convert to Guid for compatibility
        var guid = new Guid(hashBytes);
        return guid.ToString();
    }

    public bool IsPremiumFeatureAllowed(AppFeature feature)
    {
        // not a premium feature
        if (!Features.PremiumFeatures.Contains(feature))
            return true;

        // check if the current profile is premium
        return CurrentClientProfileInfo?.IsPremiumAccount == true;
    }

    public void VerifyPremiumFeature(AppFeature feature)
    {
        if (!IsPremiumFeatureAllowed(feature))
            throw PremiumOnlyException.Create(feature);
    }

    public void UpdateUi()
    {
        ApplySettings();
        UiHasChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disconnectOnDispose && ConnectionState.CanDisconnect())
            await Disconnect().Vhc();

        Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        SettingsService.BeforeSave -= SettingsBeforeSave;
        _vpnServiceManager.StateChanged -= VpnService_StateChanged;
        _vpnServiceManager.Dispose();
        _versionCheckJob.Dispose();
        _device.Dispose();
        _logService.Dispose();
        AppUiContext.OnChanged -= ActiveUiContext_OnChanged;

        base.Dispose(disposing);
    }
}