using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Core.Client.VpnServices.Manager;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Common.IpLocations;
using VpnHood.Core.Common.IpLocations.Providers;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib;

public class VpnHoodApp : Singleton<VpnHoodApp>,
    IAsyncDisposable, IJob, IRegionProvider
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
    private readonly string? _ga4MeasurementId;
    private readonly TimeSpan _versionCheckInterval;
    private readonly TimeSpan _reconnectTimeout;
    private readonly TimeSpan _autoWaitTimeout;
    private readonly TimeSpan _serverQueryTimeout;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _canExtendByRewardedAdThreshold;
    private readonly TimeSpan _sessionTimeout;
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
    private CancellationTokenSource _connectTimeoutCts = new();
    private VersionCheckResult? _versionCheckResult;
    private CultureInfo? _systemUiCulture;
    private UserSettings _oldUserSettings;
    private bool _isConnecting;
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
    public JobSection JobSection { get; }
    public TimeSpan TcpTimeout { get; set; } = ClientOptions.Default.ConnectTimeout;
    public LogService LogService { get; }
    public AppResources Resources { get; }
    public AppServices Services { get; }
    public AppSettingsService SettingsService { get; }
    public DeviceAppInfo[] InstalledApps => _device.InstalledApps;
    public LocalIpRangeLocationProvider IpRangeLocationProvider =>
        _ipRangeLocationProvider ?? throw new NotSupportedException("IpRangeLocationProvider is not supported.");

    private VpnHoodApp(IDevice device, AppOptions options)
    {
        Directory.CreateDirectory(options.StorageFolderPath); //make sure directory exists
        Resources = options.Resources;
        _device = device;
        StorageFolderPath = options.StorageFolderPath ?? throw new ArgumentNullException(nameof(options.StorageFolderPath));
        SettingsService = new AppSettingsService(StorageFolderPath);
        SettingsService.BeforeSave += SettingsBeforeSave;
        _oldUserSettings = JsonUtils.JsonClone(UserSettings);
        _appPersistState = AppPersistState.Load(Path.Combine(StorageFolderPath, FileNamePersistState));
        _useInternalLocationService = options.UseInternalLocationService;
        _useExternalLocationService = options.UseExternalLocationService;
        _locationServiceTimeout = options.LocationServiceTimeout;
        _ga4MeasurementId = options.Ga4MeasurementId;
        _versionCheckInterval = options.VersionCheckInterval;
        _reconnectTimeout = options.ReconnectTimeout;
        _autoWaitTimeout = options.AutoWaitTimeout;
        _versionCheckResult = JsonUtils.TryDeserializeFile<VersionCheckResult>(VersionCheckFilePath);
        _autoDiagnose = options.AutoDiagnose;
        _serverQueryTimeout = options.ServerQueryTimeout;
        _connectTimeout = options.ConnectTimeout;
        _allowEndPointTracker = options.AllowEndPointTracker;
        _canExtendByRewardedAdThreshold = options.CanExtendByRewardedAdThreshold;
        _disconnectOnDispose = options.DisconnectOnDispose;
        _logServiceOptions = options.LogServiceOptions;
        _trackerFactory = options.TrackerFactory ?? new BuiltInTrackerFactory();
        _sessionTimeout = options.SessionTimeout;

        // IpRangeLocationProvider
        if (options.UseInternalLocationService) {
            if (options.Resources.IpLocationZipData == null) throw new ArgumentException("Internal location service needs IpLocationZipData.");
            _ipRangeLocationProvider = new LocalIpRangeLocationProvider(
                () => new ZipArchive(new MemoryStream(options.Resources.IpLocationZipData)),
                () => _appPersistState.ClientCountryCode);
        }

        ClientProfileService = new ClientProfileService(Path.Combine(StorageFolderPath, FolderNameProfiles));
        LogService = new LogService(Path.Combine(StorageFolderPath, FileNameLog));
        Diagnoser.StateChanged += (_, _) => FireConnectionStateChanged();

        // configure update job section
        JobSection = new JobSection(new JobOptions {
            Interval = options.VersionCheckInterval,
            DueTime = options.VersionCheckInterval > TimeSpan.FromSeconds(5)
                ? TimeSpan.FromSeconds(2) // start immediately
                : options.VersionCheckInterval,
            Name = "VersionCheck"
        });

        // add default test public server if not added yet
        var builtInProfileIds = ClientProfileService.ImportBuiltInAccessKeys(options.AccessKeys);

        // remove default client profile if not exists
        if (UserSettings.ClientProfileId != null &&
            ClientProfileService.FindById(UserSettings.ClientProfileId.Value) == null)
            UserSettings.ClientProfileId = null;

        // set first built in profile as default if default is not set
        UserSettings.ClientProfileId ??= builtInProfileIds.FirstOrDefault()?.ClientProfileId;

        // set default server location if not set
        var uiProvider = options.UiProvider ?? new AppNotSupportedUiProvider();

        // initialize features
        Features = new AppFeatures {
            Version = typeof(VpnHoodApp).Assembly.GetName().Version,
            IsExcludeAppsSupported = _device.IsExcludeAppsSupported,
            IsIncludeAppsSupported = _device.IsIncludeAppsSupported,
            IsAddAccessKeySupported = options.IsAddAccessKeySupported,
            IsPremiumFlagSupported = !options.IsAddAccessKeySupported,
            IsPremiumFeaturesForced = options.IsAddAccessKeySupported,
            IsTv = device.IsTv,
            AdjustForSystemBars = options.AdjustForSystemBars,
            UpdateInfoUrl = options.UpdateInfoUrl != null ? new Uri(options.UpdateInfoUrl) : null,
            UiName = options.UiName,
            BuiltInClientProfileId = builtInProfileIds.FirstOrDefault()?.ClientProfileId,
            IsAccountSupported = options.AccountProvider != null,
            IsBillingSupported = options.AccountProvider?.BillingProvider != null,
            IsQuickLaunchSupported = uiProvider.IsQuickLaunchSupported,
            IsNotificationSupported = uiProvider.IsNotificationSupported,
            IsAlwaysOnSupported = device.IsAlwaysOnSupported,
            GaMeasurementId = options.Ga4MeasurementId,
            ClientId = CreateClientId(options.AppId, options.DeviceId ?? Settings.ClientId),
            AppId = options.AppId,
            DebugCommands = DebugCommands.All,
            IsLocalNetworkSupported = options.IsLocalNetworkSupported,
            IsDebugMode = options.IsDebugMode
        };

        // create tracker
        var tracker = options.TrackerFactory?.TryCreateTracker(new TrackerCreateParams {
            ClientId = Features.ClientId,
            ClientVersion = Features.Version,
            Ga4MeasurementId = Features.GaMeasurementId,
            UserAgent = null //not set yet
        });

        // create ad service
        var appAdService = new AppAdService(regionProvider: this,
            adProviderItems: options.AdProviderItems,
            adOptions: options.AdOptions,
            device: _device,
            tracker: tracker);

        // initialize services
        Services = new AppServices {
            CultureProvider = options.CultureProvider ?? new AppCultureProvider(this),
            AdService = appAdService,
            AccountService = options.AccountProvider != null
                ? new AppAccountService(this, options.AccountProvider)
                : null,
            UpdaterProvider = options.UpdaterProvider,
            UiProvider = uiProvider,
            Tracker = tracker
        };

        // initialize client manager
        _vpnServiceManager = new VpnServiceManager(device, appAdService, options.EventWatcherInterval);
        _vpnServiceManager.StateChanged += VpnServiceStateChanged;

        // Clear last update status if version has changed
        if (_versionCheckResult != null && _versionCheckResult.LocalVersion != Features.Version) {
            _versionCheckResult = null;
            File.Delete(VersionCheckFilePath);
        }

        // Apply settings but no error on start up
        ApplySettings();

        // schedule job
        AppUiContext.OnChanged += ActiveUiContext_OnChanged;
        JobRunner.Default.Add(this);
    }

    public void UpdateCurrentCountry(string country)
    {
        if (_appPersistState.ClientCountryCode == country) return;
        _appPersistState.ClientCountryCode = country;
        ClientProfileService.Reload();
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
                    !UserSettings.AppFilters.SequenceEqual(_oldUserSettings.AppFilters);
            }

            // set default ContinueOnCapturedContext
            VhTaskExtensions.DefaultContinueOnCapturedContext = HasDebugCommand(DebugCommands.CaptureContext);

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
                _ = Disconnect();
            }
        }
        catch (Exception ex) {
            ReportError(ex, "Could not apply settings.");
        }
    }

    private void ActiveUiContext_OnChanged(object sender, EventArgs e)
    {
        var uiContext = AppUiContext.Context;
        if (IsIdle && Services.AdService.IsPreloadAdEnabled && uiContext != null)
            _ = Services.AdService.LoadInterstitialAdAd(uiContext, CancellationToken.None);
    }

    public ClientProfileInfo? CurrentClientProfileInfo =>
        ClientProfileService.FindInfo(UserSettings.ClientProfileId ?? Guid.Empty);

    public ApiError? LastError {
        get {
            // don't show error if it is not idle
            if (!IsIdle)
                return null;

            // show last error
            if (_appPersistState.LastError != null)
                return _appPersistState.LastError;

            // Show error if diagnose has been requested and there is no error
            if (_appPersistState.HasDiagnoseRequested)
                return new NoErrorFoundException().ToApiError();

            if (_appPersistState.HasDisconnectedByUser)
                return null;

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
                SessionStatus = connectionInfo.SessionStatus?.ToAppDto(),
                SessionInfo = connectionInfo.SessionInfo?.ToAppDto(),
                ConnectionState = connectionState,
                CanConnect = connectionState.CanConnect(),
                CanDiagnose = connectionState.CanDiagnose(_appPersistState.HasDiagnoseRequested),
                CanDisconnect = connectionState.CanDisconnect(),
                IsIdle = IsIdle,
                PromptForLog = IsIdle && _appPersistState.HasDiagnoseRequested && LogService.Exists,
                LogExists = LogService.Exists,
                HasDiagnoseRequested = _appPersistState.HasDiagnoseRequested,
                HasDisconnectedByUser = _appPersistState.HasDisconnectedByUser,
                ClientCountryCode = _appPersistState.ClientCountryCode,
                ClientCountryName = VhUtils.TryGetCountryName(_appPersistState.ClientCountryCode),
                ConnectRequestTime = _appPersistState.ConnectRequestTime,
                CurrentUiCultureInfo = new UiCultureInfo(CultureInfo.DefaultThreadCurrentUICulture ?? SystemUiCulture),
                SystemUiCultureInfo = new UiCultureInfo(SystemUiCulture),
                VersionStatus = _versionCheckResult?.VersionStatus ?? VersionStatus.Unknown,
                PurchaseState = Services.AccountService?.BillingService?.PurchaseState,
                LastPublishInfo = _versionCheckResult?.GetNewerPublishInfo(),
                ClientProfile = clientProfileInfo?.ToBaseInfo(),
                LastError = IsIdle ? LastError?.ToAppDto() : null,
                SystemBarsInfo = !Features.AdjustForSystemBars && uiContext != null
                    ? Services.UiProvider.GetSystemBarsInfo(uiContext)
                    : SystemBarsInfo.Default
            };

            return appState;
        }
    }

    public Task ForceUpdateState()
    {
        return _vpnServiceManager.ForceUpdateState(CancellationToken.None);
    }

    public AppConnectionState ConnectionState {
        get {
            var clientState = ConnectionInfo.ClientState;

            if (clientState == ClientState.Disconnecting || _isDisconnecting)
                return AppConnectionState.None; // treat as none. let's service disconnect on background

            if (_isLoadingCountryIpRange || _isFindingCountryCode || clientState == ClientState.Initializing)
                return AppConnectionState.Initializing;

            if (Diagnoser.IsWorking)
                return AppConnectionState.Diagnosing;

            if (clientState == ClientState.Waiting)
                return AppConnectionState.Waiting;

            if (clientState == ClientState.WaitingForAd)
                return AppConnectionState.WaitingForAd;

            if (clientState == ClientState.Connected)
                return AppConnectionState.Connected;

            // must be at end because _isConnecting overrides clientState
            if (clientState == ClientState.Connecting || _isConnecting)
                return AppConnectionState.Connecting;

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
        return new VpnHoodApp(device, options);
    }

    public void ClearLastError()
    {
        _appPersistState.LastError = null;
        _appPersistState.LastClearedError = ConnectionInfo.Error;
        _appPersistState.HasDisconnectedByUser = false;
        _appPersistState.HasDiagnoseRequested = false;
    }

    private LogServiceOptions GetLogOptions()
    {
        var logLevel = _logServiceOptions.LogLevel;
        if (HasDebugCommand(DebugCommands.LogDebug) || Features.IsDebugMode) logLevel = LogLevel.Debug;
        if (HasDebugCommand(DebugCommands.LogTrace)) logLevel = LogLevel.Trace;
        var logOptions = new LogServiceOptions {
            LogLevel = logLevel,
            LogAnonymous = !Features.IsDebugMode && (_logServiceOptions.LogAnonymous == true || UserSettings.LogAnonymous),
            LogEventNames = LogService.GetLogEventNames(_logServiceOptions.LogEventNames, UserSettings.DebugData1 ?? "").ToArray(),
            SingleLineConsole = _logServiceOptions.SingleLineConsole,
            LogToConsole = _logServiceOptions.LogToConsole,
            LogToFile = _logServiceOptions.LogToFile,
            AutoFlush = _logServiceOptions.AutoFlush,
            CategoryName = _logServiceOptions.CategoryName
        };
        return logOptions;
    }

    private readonly AsyncLock _connectLock = new();
    public async Task Connect(ConnectOptions? connectOptions = null, CancellationToken cancellationToken = default)
    {
        connectOptions ??= new ConnectOptions();

        // protect double call
        if (!IsIdle) {
            VhLogger.Instance.LogInformation("Disconnecting due to user request to connect...");
            await Disconnect().VhConfigureAwait();
        }

        // wait for previous connection to be disposed
        using var connectLock = await _connectLock.LockAsync(cancellationToken).VhConfigureAwait();

        // protect double call. Disconnect if still in progress
        if (!IsIdle)
            await Disconnect().VhConfigureAwait();

        // create connect cancellation token
        _connectCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _connectCts.Token);
        await ConnectInternal(connectOptions, linkedCts.Token);
    }

    private async Task ConnectInternal(ConnectOptions connectOptions, CancellationToken cancellationToken)
    {
        // set use default clientProfile and serverLocation
        var orgCancellationToken = cancellationToken;
        var clientProfileId = connectOptions.ClientProfileId ?? UserSettings.ClientProfileId ?? throw new NotExistsException("ClientProfile is not set.");
        var clientProfile = ClientProfileService.Get(clientProfileId);

        try {
            var clientProfileInfo = ClientProfileService.GetInfo(clientProfileId);
            var serverLocation = connectOptions.ServerLocation ?? clientProfileInfo.SelectedLocationInfo?.ServerLocation;

            // set timeout
            _connectTimeoutCts = new CancellationTokenSource(
                Debugger.IsAttached || connectOptions.Diagnose ? Timeout.InfiniteTimeSpan : _connectTimeout);

            // Reset everything
            ClearLastError();

            // create cancellationToken after disconnecting previous connection
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _connectTimeoutCts.Token);
            cancellationToken = linkedCts.Token;

            // reset connection state
            _isConnecting = true;
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
            LogService.Start(GetLogOptions());

            // set current profile only if it has been updated to avoid unnecessary new config time
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
                    VhUtils.TryGetCountryName(await GetCurrentCountryAsync(cancellationToken).VhConfigureAwait()));


            // request features for the first time
            VhLogger.Instance.LogDebug("Requesting Features ...");
            await RequestFeatures(cancellationToken).VhConfigureAwait();

            // connect
            VhLogger.Instance.LogInformation("Client is Connecting ...");
            await ConnectInternal(clientProfile.Token,
                    serverLocation: serverLocation,
                    userAgent: connectOptions.UserAgent,
                    planId: connectOptions.PlanId,
                    accessCode: clientProfile.AccessCode,
                    allowUpdateToken: true,
                    cancellationToken: cancellationToken)
                .VhConfigureAwait();
        }
        catch (Exception ex) {
            ReportError(ex, "Could not connect.");
            // Reset server location if no server is available

            if (ex is SessionException sessionException) {
                switch (sessionException.SessionResponse.ErrorCode) {
                    case SessionErrorCode.NoServerAvailable:
                    case SessionErrorCode.PremiumLocation:
                        ClientProfileService.Update(clientProfileId,
                            new ClientProfileUpdateParams { SelectedLocation = new Patch<string?>(null) });
                        break;

                    case SessionErrorCode.AccessCodeRejected:
                        ClientProfileService.Update(clientProfileId,
                            new ClientProfileUpdateParams { AccessCode = new Patch<string?>(null) });
                        break;

                    // remove client profile if access expired
                    case SessionErrorCode.AccessExpired when clientProfile.IsForAccount:
                        ClientProfileService.Delete(clientProfile.ClientProfileId);
                        _ = Services.AccountService?.Refresh(true);
                        break;
                }
            }

            // throw OperationCanceledException if user has canceled the connection
            if (_appPersistState.HasDisconnectedByUser) {
                throw new OperationCanceledException("Connection has been canceled by the user.", ex);
            }

            // check no internet connection, use original cancellation token to avoid timeout exception
            if (_autoDiagnose && ex is not SessionException &&
                _appPersistState is { HasDisconnectedByUser: false, HasDiagnoseRequested: false }) {
                await Diagnoser.CheckPureNetwork(orgCancellationToken).VhConfigureAwait();
            }

            // throw ConnectionTimeoutException if timeout
            if (_connectTimeoutCts.IsCancellationRequested) {
                var exception = new ConnectionTimeoutException("Could not establish connection in given time.", ex);
                _ = _vpnServiceManager.Stop(); // stop client if timeout
                _appPersistState.LastError = exception.ToApiError();
                throw exception;
            }

            throw;
        }
        finally {
            _isConnecting = false;
            FireConnectionStateChanged();
        }
    }

    public bool HasDebugCommand(string command)
    {
        if (string.IsNullOrEmpty(UserSettings.DebugData1))
            return false;

        var commands = UserSettings.DebugData1.Split(' ');
        return commands.Contains(command, StringComparer.OrdinalIgnoreCase);
    }

    private async Task ConnectInternal(Token token, string? serverLocation, string? userAgent, ConnectPlanId planId,
        string? accessCode, bool allowUpdateToken, CancellationToken cancellationToken)
    {
        // show token info
        VhLogger.Instance.LogInformation("TokenId: {TokenId}, SupportId: {SupportId}",
            VhLogger.FormatId(token.TokenId), VhLogger.FormatId(token.SupportId));

        // calculate vpnAdapterIpRanges
        var vpnAdapterIpRanges = IpNetwork.All.ToIpRanges();
        if (UserSettings.UseVpnAdapterIpFilter) {
            vpnAdapterIpRanges =
                vpnAdapterIpRanges.Intersect(
                    IpFilterParser.ParseIncludes(SettingsService.IpFilterSettings.AdapterIpFilterIncludes));
            vpnAdapterIpRanges =
                vpnAdapterIpRanges.Exclude(
                    IpFilterParser.ParseExcludes(SettingsService.IpFilterSettings.AdapterIpFilterExcludes));
        }

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
            MaxDatagramChannelCount = UserSettings.MaxDatagramChannelCount,
            ConnectTimeout = TcpTimeout,
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
            CanExtendByRewardedAdThreshold = _canExtendByRewardedAdThreshold,
            AllowRewardedAd = Services.AdService.CanShowRewarded,
            ExcludeApps = UserSettings.AppFiltersMode == FilterMode.Exclude ? UserSettings.AppFilters : null,
            IncludeApps = UserSettings.AppFiltersMode == FilterMode.Include ? UserSettings.AppFilters : null,
            SessionName = CurrentClientProfileInfo?.ClientProfileName,
            LogServiceOptions = GetLogOptions(),
            Ga4MeasurementId = _ga4MeasurementId,
            Version = Features.Version,
            TrackerFactoryAssemblyQualifiedName = _trackerFactory.GetType().AssemblyQualifiedName,
            UserAgent = userAgent ?? ClientOptions.Default.UserAgent,
            DebugData1 = UserSettings.DebugData1,
            DebugData2 = UserSettings.DebugData2,
        };

        try {
            VhLogger.Instance.LogDebug(
                "Launching VpnService ... DiagnoseMode: {DiagnoseMode}, AutoDiagnose: {AutoDiagnose}",
                _appPersistState.HasDiagnoseRequested, _autoDiagnose);

            // start diagnose if requested
            if (_appPersistState.HasDiagnoseRequested) {
                var hostEndPoints = await token.ServerToken.ResolveHostEndPoints(cancellationToken)
                    .VhConfigureAwait();
                await Diagnoser.CheckEndPoints(hostEndPoints, cancellationToken).VhConfigureAwait();
                await Diagnoser.CheckPureNetwork(cancellationToken).VhConfigureAwait();
                await _vpnServiceManager.Start(clientOptions, cancellationToken).VhConfigureAwait();
                await Diagnoser.CheckVpnNetwork(cancellationToken).VhConfigureAwait();
            }
            // start client
            else {
                await _vpnServiceManager.Start(clientOptions, cancellationToken).VhConfigureAwait();
            }

            var connectionInfo = ConnectionInfo;
            if (connectionInfo.SessionInfo == null)
                throw new InvalidOperationException("Client is connected but there is no session.");

            // update access token if AccessKey is set
            if (!string.IsNullOrWhiteSpace(connectionInfo.SessionInfo.AccessKey))
                ClientProfileService.UpdateTokenByAccessKey(token.TokenId, connectionInfo.SessionInfo.AccessKey);

            // update client country
            if (!string.IsNullOrWhiteSpace(connectionInfo.SessionInfo.ClientCountry))
                _appPersistState.ClientCountryCodeByServer = connectionInfo.SessionInfo.ClientCountry;

            // check version after first connection
            _ = VersionCheck(delay: Services.AdService.ShowAdPostDelay.Add(TimeSpan.FromSeconds(1)));
        }
        catch (Exception ex) {
            if (ex is SessionException sessionException) {
                // update access token if AccessKey is set
                if (!string.IsNullOrWhiteSpace(sessionException.SessionResponse.AccessKey)) {
                    ClientProfileService.UpdateTokenByAccessKey(token.TokenId, sessionException.SessionResponse.AccessKey);
                    sessionException.SessionResponse.AccessKey = null;
                }

                // update client country
                if (!string.IsNullOrWhiteSpace(sessionException.SessionResponse.ClientCountry))
                    _appPersistState.ClientCountryCodeByServer = sessionException.SessionResponse.ClientCountry;
            }


            // try to update token from url after connection or error if ResponseAccessKey is not set
            if (!_appPersistState.HasDisconnectedByUser &&
                allowUpdateToken &&
                !VhUtils.IsNullOrEmpty(token.ServerToken.Urls) &&
                await ClientProfileService.UpdateServerTokenByUrls(token).VhConfigureAwait()) {
                token = ClientProfileService.GetToken(token.TokenId);
                await ConnectInternal(token,
                        serverLocation: serverLocation,
                        userAgent: userAgent,
                        planId: planId,
                        accessCode: accessCode,
                        allowUpdateToken: false,
                        cancellationToken: cancellationToken)
                    .VhConfigureAwait();
                return;
            }

            throw;
        }
    }

    private async Task RequestFeatures(CancellationToken cancellationToken)
    {
        // QuickLaunch
        if (AppUiContext.Context != null &&
            Services.UiProvider.IsQuickLaunchSupported &&
            Settings.IsQuickLaunchEnabled is null) {
            try {
                VhLogger.Instance.LogInformation("Prompting for Quick Launch...");
                Settings.IsQuickLaunchEnabled =
                    await Services.UiProvider.RequestQuickLaunch(AppUiContext.RequiredContext, cancellationToken)
                        .VhConfigureAwait();
            }
            catch (Exception ex) {
                ReportError(ex, "Could not add QuickLaunch.");
            }

            Settings.Save();
        }

        // Notification
        if (AppUiContext.Context != null &&
            Services.UiProvider.IsNotificationSupported &&
            Settings.IsNotificationEnabled is null) {
            try {
                VhLogger.Instance.LogInformation("Prompting for notifications...");
                Settings.IsNotificationEnabled =
                    await Services.UiProvider.RequestNotification(AppUiContext.RequiredContext, cancellationToken)
                        .VhConfigureAwait();
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
        // find the first available culture that match with the system culture
        _systemUiCulture = Services.CultureProvider.GetBestCultureInfo();

        // set default culture
        var firstSelected = Services.CultureProvider.SelectedCultures.FirstOrDefault();
        CultureInfo.CurrentUICulture = firstSelected != null ? new CultureInfo(firstSelected) : _systemUiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;

        // sync UserSettings from the System App Settings
        UserSettings.CultureCode = firstSelected;
    }

    private void SettingsBeforeSave(object sender, EventArgs e)
    {
        ApplySettings();
    }

    public string GetClientCountry() => _appPersistState.ClientCountryCode ?? RegionInfo.CurrentRegion.Name;
    public string GetClientCountryByServer() => _appPersistState.ClientCountryCodeByServer ?? GetClientCountry();

    public Task<string> GetCurrentCountryAsync(CancellationToken cancellationToken)
    {
        return GetCurrentCountryAsync(false, cancellationToken);
    }

    public async Task<string> GetCurrentCountryAsync(bool ignoreCache, CancellationToken cancellationToken)
    {
        _isFindingCountryCode = true;
        using var workScope = new AutoDispose(() => { _isFindingCountryCode = false; FireConnectionStateChanged(); });
        FireConnectionStateChanged();

        if ((_appPersistState.ClientCountryCode == null || ignoreCache) && _useExternalLocationService) {
            try {
                using var httpClient = new HttpClient();
                const string userAgent = "VpnHood-Client";
                var providers = new List<IIpLocationProvider> {
                    new CloudflareLocationProvider(httpClient, userAgent),
                    new IpLocationIoProvider(httpClient, userAgent, apiKey: null)
                };

                // InternalLocationService needs current ip from external service, so it is inside the if block
                if (_useInternalLocationService)
                    providers.Add(IpRangeLocationProvider);

                var compositeProvider = new CompositeIpLocationProvider(VhLogger.Instance, providers, providerTimeout: _locationServiceTimeout);
                var ipLocation = await compositeProvider.GetCurrentLocation(cancellationToken).VhConfigureAwait();
                UpdateCurrentCountry(ipLocation.CountryCode);
            }
            catch (Exception ex) {
                ReportError(ex, "Could not find country code.");
            }
        }

        // return last country
        return _appPersistState.ClientCountryCode ?? RegionInfo.CurrentRegion.Name;
    }

    private void VpnServiceStateChanged(object sender, EventArgs e)
    {
        // clear last error we get out of idle state
        if (!IsIdle)
            _appPersistState.LastClearedError = null;

        // cancel connect timeout if client reached to waiting ad state
        if (ConnectionInfo.ClientState == ClientState.WaitingForAd)
            _connectTimeoutCts.CancelAfter(Timeout.InfiniteTimeSpan);

        // fire connection state changed
        FireConnectionStateChanged();
    }

    public async Task Disconnect()
    {
        VhLogger.Instance.LogInformation("User requested to disconnect.");
        _isDisconnecting = true;
        using var workScope = new AutoDispose(() => { _isDisconnecting = false; FireConnectionStateChanged(); });
        _appPersistState.HasDisconnectedByUser = true;
        _connectCts.Cancel();
        await _vpnServiceManager.Stop();
    }

    public void VersionCheckPostpone()
    {
        // version status is unknown when app container can do it
        _versionCheckResult = null;
        if (File.Exists(VersionCheckFilePath))
            File.Delete(VersionCheckFilePath);

        // set latest ignore time
        _appPersistState.UpdateIgnoreTime = DateTime.Now;
    }

    private readonly AsyncLock _versionCheckLock = new();

    public async Task VersionCheck(bool force = false, TimeSpan? delay = null)
    {
        using var lockAsync = await _versionCheckLock.LockAsync().VhConfigureAwait();
        if (!force && _appPersistState.UpdateIgnoreTime + _versionCheckInterval > DateTime.Now)
            return;

        // wait for delay. Useful for waiting for ad to send its tracker
        if (delay != null)
            await Task.Delay(delay.Value).VhConfigureAwait();

        // check version by app container
        try {
            if (AppUiContext.Context != null && Services.UpdaterProvider != null &&
                await Services.UpdaterProvider.Update(AppUiContext.RequiredContext).VhConfigureAwait()) {
                VersionCheckPostpone();
                return;
            }
        }
        catch (Exception ex) {
            ReportWarning(ex, "Could not check version by VersionCheck.");
        }

        // check version by UpdateInfoUrl
        _versionCheckResult = await VersionCheckByUpdateInfo().VhConfigureAwait();

        // save the result
        if (_versionCheckResult != null)
            await File.WriteAllTextAsync(VersionCheckFilePath, JsonSerializer.Serialize(_versionCheckResult))
                .VhConfigureAwait();

        else if (File.Exists(VersionCheckFilePath))
            File.Delete(VersionCheckFilePath);
    }

    private async Task<VersionCheckResult?> VersionCheckByUpdateInfo()
    {
        try {
            if (Features.UpdateInfoUrl == null)
                return null; // no update info url. Job done

            VhLogger.Instance.LogDebug("Retrieving the latest publish info...");

            using var httpClient = new HttpClient();
            var publishInfoJson = await httpClient.GetStringAsync(Features.UpdateInfoUrl).VhConfigureAwait();
            var latestPublishInfo = JsonUtils.Deserialize<PublishInfo>(publishInfoJson);
            VersionStatus versionStatus;

            // Check version
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
            ReportWarning(ex, "Could not retrieve the latest publish info information.");
            return null; // could not retrieve the latest publish info. try later
        }
    }

    public async Task<IpRangeOrderedList> GetIncludeIpRanges(CancellationToken cancellationToken)
    {
        // calculate vpnAdapterIpRanges
        var ipRanges = IpNetwork.All.ToIpRanges();
        if (UserSettings.UseAppIpFilter) {
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

            var countryCode = await GetCurrentCountryAsync(true, cancellationToken).VhConfigureAwait();
            var countryIpRanges = await IpRangeLocationProvider.GetIpRanges(countryCode).VhConfigureAwait();
            VhLogger.Instance.LogInformation("Client CountryCode is: {CountryCode}",
                VhUtils.TryGetCountryName(_appPersistState.ClientCountryCode));
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

    public async Task ExtendByRewardedAd(CancellationToken cancellationToken)
    {
        // save variable to prevent null reference exception
        var connectionInfo = _vpnServiceManager.ConnectionInfo;

        if (Services.AdService.CanShowRewarded != true)
            throw new InvalidOperationException("Rewarded ad is not supported in this app.");

        if (connectionInfo.SessionInfo == null ||
            connectionInfo is not { ClientState: ClientState.Connected })
            throw new InvalidOperationException("Could not show ad. The VPN is not connected.");

        if (connectionInfo.SessionStatus?.CanExtendByRewardedAd != true)
            throw new InvalidOperationException("Could not extend this session by a rewarded ad at this time.");

        // use client manager so it can exclude ad data from the session
        var adResult = await Services.AdService.ShowRewarded(AppUiContext.RequiredContext,
            connectionInfo.SessionInfo.SessionId, cancellationToken);

        await _vpnServiceManager.SendRewardedAdResult(adResult, cancellationToken);
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

        // update current profile if the current profile is not exists
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
            if (File.Exists(LogService.LogFilePath)) {
                await using var appLogStream = new FileStream(LogService.LogFilePath,
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
        await write.WriteLineAsync("");
        await write.WriteLineAsync("-----------------------");
        await write.WriteLineAsync("VPN Service Log");
        await write.WriteLineAsync("-----------------------");
        await write.FlushAsync();

        try {
            if (File.Exists(_vpnServiceManager.LogFilePath)) {
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
        Services.Tracker?.VhTrackErrorAsync(ex, message, action);
        VhLogger.Instance.LogError(ex, message);
    }

    private void ReportWarning(Exception ex, string message, [CallerMemberName] string action = "n/a")
    {
        Services.Tracker?.VhTrackWarningAsync(ex, message, action);
        VhLogger.Instance.LogWarning(ex, message);
    }

    public Task RunJob()
    {
        return VersionCheck();
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

    public void UpdateUi()
    {
        ApplySettings();
        UiHasChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disconnectOnDispose && ConnectionState.CanDisconnect())
            await Disconnect().VhConfigureAwait();

        _vpnServiceManager.Dispose();
        _vpnServiceManager.StateChanged -= VpnServiceStateChanged;

        await _device.DisposeAsync();
        LogService.Dispose();
        DisposeSingleton();
        AppUiContext.OnChanged -= ActiveUiContext_OnChanged;
    }
}