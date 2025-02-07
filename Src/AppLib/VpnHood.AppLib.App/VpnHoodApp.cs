using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ga4.Trackers;
using Ga4.Trackers.Ga4Tags;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.DtoConverters;
using VpnHood.AppLib.Providers;
using VpnHood.AppLib.Services.Accounts;
using VpnHood.AppLib.Settings;
using VpnHood.AppLib.Utils;
using VpnHood.Core.Common.Trackers;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Diagnosing;
using VpnHood.Core.Common.ApiClients;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.IpLocations;
using VpnHood.Core.Common.IpLocations.Providers;
using VpnHood.Core.Common.Jobs;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Net;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.AppLib.Services.Logging;
using VpnHood.AppLib.Services.Ads;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Manager;
using VpnHood.Core.Tunneling.Factory;

namespace VpnHood.AppLib;

public class VpnHoodApp : Singleton<VpnHoodApp>,
    IAsyncDisposable, IJob, IRegionProvider
{
    private const string FileNameLog = "log.txt";
    private const string FileNamePersistState = "state.json";
    private const string FolderNameProfiles = "profiles";
    private readonly bool _useInternalLocationService;
    private readonly bool _useExternalLocationService;
    private readonly string? _ga4MeasurementId;
    private bool _hasConnectRequested;
    private bool _hasDisconnectRequested;
    private bool _hasDiagnoseRequested;
    private bool _hasDisconnectedByUser;
    private Guid? _activeClientProfileId;
    private DateTime? _connectRequestTime;
    private bool _isConnecting;
    private bool _isDisconnecting;
    private ConnectionInfo? _lastConnectionInfo;
    private AppConnectionState _lastConnectionState;
    private bool _isLoadingCountryIpRange;
    private bool _isFindingCountryCode;
    private readonly TimeSpan _versionCheckInterval;
    private readonly AppPersistState _appPersistState;
    private readonly TimeSpan _reconnectTimeout;
    private readonly TimeSpan _autoWaitTimeout;
    private readonly TimeSpan _serverQueryTimeout;
    private CancellationTokenSource? _connectCts;
    private VersionCheckResult? _versionCheckResult;
    private VpnHoodClientManager? _clientManager;
    private readonly bool _logVerbose;
    private readonly bool? _logAnonymous;
    private UserSettings _oldUserSettings;
    private readonly bool _autoDiagnose;
    private readonly bool _allowEndPointTracker;
    private readonly TimeSpan _canExtendByRewardedAdThreshold;
    private CultureInfo? _systemUiCulture;
    private string? _requestedServerLocation;
    private readonly TimeSpan? _eventWatcherInterval;

    private ConnectionInfo? LastConnectionInfo => _clientManager?.ConnectionInfo ?? _lastConnectionInfo;
    private string VersionCheckFilePath => Path.Combine(StorageFolderPath, "version.json");
    public string TempFolderPath => Path.Combine(StorageFolderPath, "Temp");
    public event EventHandler? ConnectionStateChanged;
    public event EventHandler? UiHasChanged;
    public LocalIpRangeLocationProvider IpRangeLocationProvider { get; }
    public bool IsIdle => ConnectionState == AppConnectionState.None;
    public TimeSpan SessionTimeout { get; set; }
    public Diagnoser Diagnoser { get; set; } = new();
    public string StorageFolderPath { get; }
    public AppSettings Settings => SettingsService.AppSettings;
    public UserSettings UserSettings => SettingsService.AppSettings.UserSettings;
    public AppFeatures Features { get; }
    public ClientProfileService ClientProfileService { get; }
    public IDevice Device { get; }
    public JobSection JobSection { get; }
    public TimeSpan TcpTimeout { get; set; } = ClientOptions.Default.ConnectTimeout;
    public AppLogService LogService { get; }
    public AppResource Resource { get; }
    public AppServices Services { get; }
    public DateTime? ConnectedTime { get; private set; }
    public AppSettingsService SettingsService { get; }

    private VpnHoodApp(IDevice device, AppOptions options)
    {
        device.StartedAsService += DeviceOnStartedAsService;
        Directory.CreateDirectory(options.StorageFolderPath); //make sure directory exists
        Resource = options.Resource;
        Device = device;
        StorageFolderPath = options.StorageFolderPath ?? throw new ArgumentNullException(nameof(options.StorageFolderPath));
        SettingsService = new AppSettingsService(StorageFolderPath);
        SettingsService.BeforeSave += SettingsBeforeSave;
        _oldUserSettings = VhUtil.JsonClone(UserSettings);
        _appPersistState = AppPersistState.Load(Path.Combine(StorageFolderPath, FileNamePersistState));
        _useInternalLocationService = options.UseInternalLocationService;
        _useExternalLocationService = options.UseExternalLocationService;
        _ga4MeasurementId = options.Ga4MeasurementId;
        _versionCheckInterval = options.VersionCheckInterval;
        _reconnectTimeout = options.ReconnectTimeout;
        _autoWaitTimeout = options.AutoWaitTimeout;
        _versionCheckResult = VhUtil.JsonDeserializeFile<VersionCheckResult>(VersionCheckFilePath);
        _logVerbose = options.LogVerbose;
        _logAnonymous = options.LogAnonymous;
        _autoDiagnose = options.AutoDiagnose;
        _serverQueryTimeout = options.ServerQueryTimeout;
        _allowEndPointTracker = options.AllowEndPointTracker;
        _canExtendByRewardedAdThreshold = options.CanExtendByRewardedAdThreshold;
        _eventWatcherInterval = options.EventWatcherInterval;

        SessionTimeout = options.SessionTimeout;
        Diagnoser.StateChanged += (_, _) => FireConnectionStateChanged();
        LogService = new AppLogService(Path.Combine(StorageFolderPath, FileNameLog), options.SingleLineConsoleLog);
        ClientProfileService = new ClientProfileService(Path.Combine(StorageFolderPath, FolderNameProfiles));
        IpRangeLocationProvider = new LocalIpRangeLocationProvider(
            () => new ZipArchive(new MemoryStream(AppLib.Resource.IpLocations)),
            () => _appPersistState.ClientCountryCode);

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
        if (UserSettings.ClientProfileId != null && ClientProfileService.FindById(UserSettings.ClientProfileId.Value) == null)
            UserSettings.ClientProfileId = null;

        // set first built in profile as default if default is not set
        UserSettings.ClientProfileId ??= builtInProfileIds.FirstOrDefault()?.ClientProfileId;

        // set default server location if not set
        var uiProvider = options.UiProvider ?? new AppNotSupportedUiProvider();

        // initialize features
        Features = new AppFeatures {
            Version = typeof(VpnHoodApp).Assembly.GetName().Version,
            IsExcludeAppsSupported = Device.IsExcludeAppsSupported,
            IsIncludeAppsSupported = Device.IsIncludeAppsSupported,
            IsAddAccessKeySupported = options.IsAddAccessKeySupported,
            IsPremiumFlagSupported = !options.IsAddAccessKeySupported,
            IsPremiumFeaturesForced = options.IsAddAccessKeySupported,
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

        // create ad service
        var appAdService = new AppAdService(regionProvider: this,
            adProviderItems: options.AdProviderItems,
            adOptions: options.AdOptions,
            tracker: options.Tracker);

        // initialize services
        Services = new AppServices {
            CultureProvider = options.CultureProvider ?? new AppCultureProvider(this),
            AdService = appAdService,
            AccountService = options.AccountProvider != null ? new AppAccountService(this, options.AccountProvider) : null,
            UpdaterProvider = options.UpdaterProvider,
            UiProvider = uiProvider,
            Tracker = options.Tracker
        };

        // Clear last update status if version has changed
        if (_versionCheckResult != null && _versionCheckResult.LocalVersion != Features.Version) {
            _versionCheckResult = null;
            File.Delete(VersionCheckFilePath);
        }

        // Apply settings but no error on start up
        ApplySettings();

        // schedule job
        ActiveUiContext.OnChanged += ActiveUiContext_OnChanged;
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
            if (_clientManager != null) {
                var updateParams = new ClientUpdateParams {
                    UseTcpOverTun = HasDebugCommand(DebugCommands.UseTcpOverTun),
                    UseUdpChannel = UserSettings.UseUdpChannel,
                    DropUdp = HasDebugCommand(DebugCommands.DropUdp) || UserSettings.DropUdp,
                    DropQuic = UserSettings.DropQuic
                };
                // it is not important to take effect immediately
                _ = _clientManager.Reconfigure(updateParams);

                // check is disconnect required
                disconnectRequired =
                    (UserSettings.UsePacketCaptureIpFilter != _oldUserSettings.UsePacketCaptureIpFilter) ||
                    (UserSettings.UseAppIpFilter != _oldUserSettings.UseAppIpFilter) ||
                    (UserSettings.TunnelClientCountry != _oldUserSettings.TunnelClientCountry) ||
                    (UserSettings.ClientProfileId != _activeClientProfileId) ||
                    (UserSettings.IncludeLocalNetwork != _oldUserSettings.IncludeLocalNetwork) ||
                    (UserSettings.AppFiltersMode != _oldUserSettings.AppFiltersMode) ||
                    (!UserSettings.AppFilters.SequenceEqual(_oldUserSettings.AppFilters));
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
            _oldUserSettings = VhUtil.JsonClone(UserSettings);

            // disconnect
            if (state.CanDisconnect && disconnectRequired) {
                VhLogger.Instance.LogInformation("Disconnecting due to the settings change...");
                _ = Disconnect(true);
            }
        }
        catch (Exception ex) {
            ReportError(ex, "Could not apply settings.");
        }
    }

    private ITracker CreateBuildInTracker(string? userAgent)
    {
        if (string.IsNullOrEmpty(_ga4MeasurementId))
            throw new InvalidOperationException("AppGa4MeasurementId is required to create a built-in tracker.");

        var tracker = new Ga4TagTracker {
            MeasurementId = _ga4MeasurementId,
            SessionCount = 1,
            ClientId = Settings.ClientId,
            SessionId = Guid.NewGuid().ToString(),
            UserProperties = new Dictionary<string, object> { { "client_version", Features.Version.ToString(3) } }
        };

        if (!string.IsNullOrEmpty(userAgent))
            tracker.UserAgent = userAgent;

        _ = tracker.Track(new TrackEvent { EventName = TrackEventNames.SessionStart });

        return tracker;
    }

    private void ActiveUiContext_OnChanged(object sender, EventArgs e)
    {
        var uiContext = ActiveUiContext.Context;
        if (IsIdle && Services.AdService.IsPreloadAdEnabled && uiContext != null)
            _ = Services.AdService.LoadAd(uiContext, CancellationToken.None);
    }

    public ClientProfileInfo? CurrentClientProfileInfo =>
        ClientProfileService.FindInfo(UserSettings.ClientProfileId ?? Guid.Empty);

    public AppState State {
        get {
            var clientProfileInfo = CurrentClientProfileInfo;
            var connectionInfo = _clientManager?.ConnectionInfo;
            var connectionState = ConnectionState;

            var appState = new AppState {
                ConfigTime = Settings.ConfigTime,
                SessionStatus = connectionInfo?.SessionStatus?.ToAppDto(),
                SessionInfo = connectionInfo?.SessionInfo?.ToAppDto(),
                ConnectionState = connectionState,
                IsIdle = IsIdle,
                CanConnect = connectionState is AppConnectionState.None,
                CanDiagnose = connectionState is AppConnectionState.None ||
                              (!_hasDiagnoseRequested && (connectionState is AppConnectionState.Connected or AppConnectionState.Connecting)),
                CanDisconnect = !_isDisconnecting && (connectionState
                    is AppConnectionState.Connected or AppConnectionState.Connecting
                    or AppConnectionState.Diagnosing or AppConnectionState.Waiting),
                PromptForLog = IsIdle && _hasDiagnoseRequested && LogService.Exists,
                LogExists = LogService.Exists,
                LastError = _appPersistState.LastError,
                HasDiagnoseRequested = _hasDiagnoseRequested,
                HasDisconnectedByUser = _hasDisconnectedByUser,
                HasProblemDetected = _hasConnectRequested && IsIdle && (_hasDiagnoseRequested || _appPersistState.LastError != null),
                ClientCountryCode = _appPersistState.ClientCountryCode,
                ClientCountryName = VhUtil.TryGetCountryName(_appPersistState.ClientCountryCode),
                ConnectRequestTime = _connectRequestTime,
                CurrentUiCultureInfo = new UiCultureInfo(CultureInfo.DefaultThreadCurrentUICulture ?? SystemUiCulture),
                SystemUiCultureInfo = new UiCultureInfo(SystemUiCulture),
                VersionStatus = _versionCheckResult?.VersionStatus ?? VersionStatus.Unknown,
                PurchaseState = Services.AccountService?.BillingService?.PurchaseState,
                LastPublishInfo = _versionCheckResult?.VersionStatus is VersionStatus.Deprecated or VersionStatus.Old
                    ? _versionCheckResult.PublishInfo
                    : null,
                ClientProfile = clientProfileInfo?.ToBaseInfo()
            };

            return appState;
        }
    }

    public Task ForceUpdateState()
    {
        var clientManager = _clientManager;
        return clientManager != null ? clientManager.ForceUpdateState() : Task.CompletedTask;
    }

    public AppConnectionState ConnectionState {
        get {
            var clientState = _clientManager?.ConnectionInfo?.ClientState;
            if (_isLoadingCountryIpRange || _isFindingCountryCode) return AppConnectionState.Initializing;
            if (Diagnoser.IsWorking) return AppConnectionState.Diagnosing;
            if (_isDisconnecting || clientState == ClientState.Disconnecting) return AppConnectionState.Disconnecting;
            if (_isConnecting || clientState == ClientState.Connecting) return AppConnectionState.Connecting;
            if (clientState == ClientState.Waiting) return AppConnectionState.Waiting;
            if (clientState == ClientState.Connected) return AppConnectionState.Connected;
            return AppConnectionState.None;
        }
    }

    private void FireConnectionStateChanged()
    {
        // check changed state
        var connectionState = ConnectionState;
        if (connectionState == _lastConnectionState) return;
        _lastConnectionState = connectionState;
        try {
            ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) {
            ReportError(ex, "Could not fire app's ConnectionStateChanged.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Disconnect().VhConfigureAwait();
        Device.Dispose();
        LogService.Dispose();
        DisposeSingleton();
        ActiveUiContext.OnChanged -= ActiveUiContext_OnChanged;
    }

    public static VpnHoodApp Init(IDevice device, AppOptions options)
    {
        return new VpnHoodApp(device, options);
    }

    private void DeviceOnStartedAsService(object sender, EventArgs e)
    {
        if (CurrentClientProfileInfo == null) {
            var ex = new Exception("Could not start as service. No server is selected.");
            _appPersistState.LastError = new ApiError(ex);
            throw ex;
        }

        _ = Connect(CurrentClientProfileInfo.ClientProfileId);
    }

    public void ClearLastError()
    {
        _appPersistState.LastError = null;
        _hasDiagnoseRequested = false;

    }

    private readonly AsyncLock _connectLock = new();
    public async Task Connect(
        Guid? clientProfileId = null,
        ConnectPlanId planId = ConnectPlanId.Normal,
        string? serverLocation = null,
        bool diagnose = false,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        using var lockAsync = await _connectLock.LockAsync(cancellationToken).VhConfigureAwait();

        // set use default clientProfile and serverLocation
        clientProfileId ??= UserSettings.ClientProfileId ?? throw new NotExistsException("ClientProfile is not set.");
        var clientProfile = ClientProfileService.Get(clientProfileId.Value);
        var clientProfileInfo = ClientProfileService.GetInfo(clientProfileId.Value);
        serverLocation ??= clientProfileInfo.SelectedLocationInfo?.ServerLocation;

        // protect double call
        if (!IsIdle) {
            if (_activeClientProfileId == clientProfileId &&
                diagnose == _hasDiagnoseRequested && // client may request diagnose the current connection
                string.Equals(_requestedServerLocation, serverLocation, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Connection is already in progress.");

            // make sure current session has been disconnected and packet-capture has been released
            VhLogger.Instance.LogInformation("Disconnecting due to user request to connect via another profile...");
            await Disconnect(true).VhConfigureAwait();
        }

        // reset connection state
        try {
            _isConnecting = true;
            _requestedServerLocation = serverLocation; // used to prevent double request
            _hasDisconnectedByUser = false;
            _hasConnectRequested = true;
            _hasDisconnectRequested = false;
            _hasDiagnoseRequested = diagnose;
            _connectRequestTime = DateTime.Now;
            _appPersistState.LastError = null;
            FireConnectionStateChanged();

            // initialize built-in tracker after acquire userAgent
            if (Services.Tracker == null && UserSettings.AllowAnonymousTracker &&
                !string.IsNullOrEmpty(_ga4MeasurementId))
                Services.Tracker = CreateBuildInTracker(userAgent);

            // prepare logger
            LogService.Start(new AppLogOptions {
                LogEventNames = AppLogService.GetLogEventNames(
                    verbose: _logVerbose || HasDebugCommand(DebugCommands.Verbose),
                    diagnose: diagnose || HasDebugCommand(DebugCommands.FullLog),
                    debugCommand: UserSettings.DebugData1),
                LogAnonymous = _logAnonymous ?? UserSettings.LogAnonymous,
                LogToConsole = true,
                LogToFile = true,
                AutoFlush = true,
                LogLevel = _logVerbose || diagnose ? LogLevel.Trace : LogLevel.Information
            });

            // set current profile only if it has been updated to avoid unnecessary new config time
            if (clientProfile.ClientProfileId != UserSettings.ClientProfileId || serverLocation != clientProfileInfo.SelectedLocationInfo?.ServerLocation) {
                clientProfile = ClientProfileService.Update(clientProfileId.Value, new ClientProfileUpdateParams { SelectedLocation = serverLocation });
                UserSettings.ClientProfileId = clientProfile.ClientProfileId;
                Settings.Save();
            }

            _activeClientProfileId = UserSettings.ClientProfileId;

            // log general info
            VhLogger.Instance.LogInformation("AppVersion: {AppVersion}, AppId: {Features.AppId}",
                GetType().Assembly.GetName().Version, Features.AppId);
            VhLogger.Instance.LogInformation("Time: {Time}", DateTime.UtcNow.ToString("u", new CultureInfo("en-US")));
            VhLogger.Instance.LogInformation("OS: {OsInfo}", Device.OsInfo);
            VhLogger.Instance.LogInformation("UserAgent: {userAgent}", userAgent);
            VhLogger.Instance.LogInformation("UserSettings: {UserSettings}",
                JsonSerializer.Serialize(UserSettings, new JsonSerializerOptions { WriteIndented = true }));
            if (diagnose) // log country name
                VhLogger.Instance.LogInformation("CountryCode: {CountryCode}",
                    VhUtil.TryGetCountryName(await GetCurrentCountryAsync(cancellationToken).VhConfigureAwait()));

            VhLogger.Instance.LogInformation("Client is Connecting ...");

            // create cancellationToken
            _connectCts = new CancellationTokenSource();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _connectCts.Token);
            cancellationToken = linkedCts.Token;

            // request features for the first time
            await RequestFeatures(cancellationToken).VhConfigureAwait();

            // ReSharper disable once DisposeOnUsingVariable
            lockAsync.Dispose(); //let new request come cancel this one

            // connect
            await ConnectInternal(clientProfile.Token,
                    serverLocation: serverLocation,
                    userAgent: userAgent,
                    planId: planId,
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
                        ClientProfileService.Update(clientProfileId.Value, new ClientProfileUpdateParams { SelectedLocation = new Patch<string?>(null) });
                        break;

                    case SessionErrorCode.AccessCodeRejected:
                        ClientProfileService.Update(clientProfileId.Value, new ClientProfileUpdateParams { AccessCode = new Patch<string?>(null) });
                        break;

                    // remove client profile if access expired
                    case SessionErrorCode.AccessExpired when clientProfile.IsForAccount:
                        ClientProfileService.Delete(clientProfile.ClientProfileId);
                        _ = Services.AccountService?.Refresh(true);
                        break;
                }
            }

            // user may disconnect before connection closed
            // don't set any error message if user has disconnected manually
            if (!_hasDisconnectedByUser) {
                _appPersistState.LastError = ex is OperationCanceledException
                    ? new ApiError(new Exception("Could not connect to any server.", ex))
                    : new ApiError(ex);
            }

            // don't wait for disconnect, it may cause deadlock
            _ = Disconnect();

            // throw OperationCanceledException if user has canceled the connection
            if (_hasDisconnectedByUser)
                throw new OperationCanceledException("Connection has been canceled by the user.", ex);

            throw;
        }
        finally {
            _isConnecting = false;
            FireConnectionStateChanged();
        }
    }

    private async Task<IPacketCapture> CreatePacketCapture()
    {
        if (HasDebugCommand(DebugCommands.NullCapture)) {
            VhLogger.Instance.LogWarning("Using NullPacketCapture. No packet will go through the VPN.");
            return new NullPacketCapture();
        }

        // create packet capture
        var packetCapture = await Device.CreatePacketCapture(ActiveUiContext.Context).VhConfigureAwait();

        // init packet capture
        if (packetCapture.IsMtuSupported)
            packetCapture.Mtu = TunnelDefaults.MtuWithoutFragmentation;

        // App filters
        if (packetCapture.CanExcludeApps && UserSettings.AppFiltersMode == FilterMode.Exclude)
            packetCapture.ExcludeApps = UserSettings.AppFilters;

        if (packetCapture.CanIncludeApps && UserSettings.AppFiltersMode == FilterMode.Include)
            packetCapture.IncludeApps = UserSettings.AppFilters;

        return packetCapture;
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

        // calculate packetCaptureIpRanges
        var packetCaptureIpRanges = IpNetwork.All.ToIpRanges();
        if (UserSettings.UsePacketCaptureIpFilter) {
            packetCaptureIpRanges = packetCaptureIpRanges.Intersect(IpFilterParser.ParseIncludes(SettingsService.IpFilterSettings.PacketCaptureIpFilterIncludes));
            packetCaptureIpRanges = packetCaptureIpRanges.Exclude(IpFilterParser.ParseExcludes(SettingsService.IpFilterSettings.PacketCaptureIpFilterExcludes));
        }

        // create clientOptions
        var clientOptions = new ClientOptions {
            SessionTimeout = SessionTimeout,
            ReconnectTimeout = _reconnectTimeout,
            AutoWaitTimeout = _autoWaitTimeout,
            IncludeLocalNetwork = UserSettings.IncludeLocalNetwork && Features.IsLocalNetworkSupported,
            IncludeIpRanges = await GetIncludeIpRanges(cancellationToken),
            PacketCaptureIncludeIpRanges = packetCaptureIpRanges,
            MaxDatagramChannelCount = UserSettings.MaxDatagramChannelCount,
            ConnectTimeout = TcpTimeout,
            ServerQueryTimeout = _serverQueryTimeout,
            DropUdp = HasDebugCommand(DebugCommands.DropUdp) || UserSettings.DropUdp,
            DropQuic = UserSettings.DropQuic,
            ServerLocation = ServerLocationInfo.IsAutoLocation(serverLocation) ? null : serverLocation,
            PlanId = planId,
            AccessCode = accessCode,
            UseTcpOverTun = HasDebugCommand(DebugCommands.UseTcpOverTun),
            UseUdpChannel = UserSettings.UseUdpChannel,
            DomainFilter = UserSettings.DomainFilter,
            ForceLogSni = LogService.LogEvents.Contains(nameof(GeneralEventId.Sni), StringComparer.OrdinalIgnoreCase),
            AllowAnonymousTracker = UserSettings.AllowAnonymousTracker,
            AllowEndPointTracker = UserSettings.AllowAnonymousTracker && _allowEndPointTracker,
            AllowTcpReuse = !HasDebugCommand(DebugCommands.NoTcpReuse),
            Tracker = Services.Tracker,
            CanExtendByRewardedAdThreshold = _canExtendByRewardedAdThreshold,
            AllowRewardedAd = Services.AdService.CanShowRewarded
        };

        if (userAgent != null) clientOptions.UserAgent = userAgent;

        // make sure the previous client has been released
        if (_clientManager != null)
            throw new Exception("Last client has not been disposed properly.");

        // Create Client with a new PacketCapture
        VhLogger.Instance.LogInformation("Creating PacketCapture ...");
        var packetCapture = await CreatePacketCapture().VhConfigureAwait();
        packetCapture.SessionName = CurrentClientProfileInfo?.ClientProfileName;
        VpnHoodClientManager? clientManager = null;

        try {
            VhLogger.Instance.LogTrace("Creating VpnHood Client engine ...");

            // set eventWatcherInterval
            var eventWatcherInterval = _eventWatcherInterval == null && ConnectionStateChanged != null ?
                TimeSpan.FromSeconds(1) : _eventWatcherInterval;

            // create client controller
            clientManager = VpnHoodClientManager.Create(packetCapture, new SocketFactory(),
                Services.AdService, Features.ClientId, token, clientOptions, eventWatcherInterval);
            clientManager.StateChanged += Client_StateChanged;
            _clientManager = clientManager;

            VhLogger.Instance.LogTrace(
                "Engine is connecting... DiagnoseMode: {DiagnoseMode}, AutoDiagnose: {AutoDiagnose}",
                _hasDiagnoseRequested, _autoDiagnose);

            if (_hasDiagnoseRequested)
                await Diagnoser.Diagnose(clientManager.Client, cancellationToken).VhConfigureAwait();
            else if (_autoDiagnose)
                await Diagnoser.Connect(clientManager.Client, cancellationToken).VhConfigureAwait();
            else
                await clientManager.Start(cancellationToken).VhConfigureAwait();

            // set connected time
            ConnectedTime = DateTime.Now;
            UpdateStatusByCreatedClient(clientManager, token, null);

            // check version after first connection
            _ = VersionCheck();
        }
        catch (Exception ex) when (clientManager is not null) {
            UpdateStatusByCreatedClient(clientManager, token, ex);

            // dispose client
            clientManager.StateChanged -= Client_StateChanged;
            await clientManager.DisposeAsync().VhConfigureAwait();
            _clientManager = null;

            // try to update token from url after connection or error if ResponseAccessKey is not set
            // check _client is not null to make sure 
            if (ex is not OperationCanceledException &&
                allowUpdateToken &&
                !VhUtil.IsNullOrEmpty(token.ServerToken.Urls) &&
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

            // save lastSessionStatus before destroying the client
            _lastConnectionInfo = clientManager.ConnectionInfo;
            throw;
        }
        catch (Exception) {
            packetCapture.Dispose(); // don't miss to dispose when there is no client to handle it
            throw;
        }
    }

    private void UpdateStatusByCreatedClient(VpnHoodClientManager clientManager, Token token, Exception? ex)
    {
        // update access token if AccessKey is set
        var connectionInfo = clientManager.ConnectionInfo;
        var accessKey = connectionInfo?.SessionInfo?.AccessKey;

        // update token by access key
        if (accessKey == null && connectionInfo?.Error?.Data.ContainsKey("AccessKey") == true) {
            accessKey = connectionInfo.Error?.Data["AccessKey"];
        }

        if (!string.IsNullOrWhiteSpace(accessKey)) {
            ClientProfileService.UpdateTokenByAccessKey(token.TokenId, accessKey);
        }

        var clientCountry = connectionInfo?.SessionInfo?.ClientCountry;
        if (!string.IsNullOrWhiteSpace(clientCountry))
            _appPersistState.ClientCountryCodeByServer = clientCountry;

        // make sure AccessKey is deleted
        if (connectionInfo?.SessionInfo?.AccessKey != null)
            connectionInfo.SessionInfo.AccessKey = null;

        if (connectionInfo?.Error?.Data.ContainsKey("AccessKey") == true)
            connectionInfo.Error?.Data.Remove("AccessKey");

        if (ex?.Data.Contains("AccessKey") == true)
            ex.Data.Remove("AccessKey");

        if (ex is SessionException sessionException)
            sessionException.SessionResponse.AccessKey = null;
    }

    private async Task RequestFeatures(CancellationToken cancellationToken)
    {
        // QuickLaunch
        if (ActiveUiContext.Context != null &&
            Services.UiProvider.IsQuickLaunchSupported &&
            Settings.IsQuickLaunchEnabled is null) {
            try {
                Settings.IsQuickLaunchEnabled =
                    await Services.UiProvider.RequestQuickLaunch(ActiveUiContext.RequiredContext, cancellationToken)
                        .VhConfigureAwait();
            }
            catch (Exception ex) {
                ReportError(ex, "Could not add QuickLaunch.");
            }

            Settings.Save();
        }

        // Notification
        if (ActiveUiContext.Context != null &&
            Services.UiProvider.IsNotificationSupported &&
            Settings.IsNotificationEnabled is null) {
            try {
                Settings.IsNotificationEnabled =
                    await Services.UiProvider.RequestNotification(ActiveUiContext.RequiredContext, cancellationToken)
                        .VhConfigureAwait();
            }
            catch (Exception ex) {
                ReportError(ex, "Could not enable Notification.");
            }

            Settings.Save();
        }
    }

    public CultureInfo SystemUiCulture =>
        _systemUiCulture ?? new CultureInfo(Services.CultureProvider.SystemCultures.FirstOrDefault() ?? CultureInfo.InstalledUICulture.Name);

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

        if ((_appPersistState.ClientCountryCode == null || ignoreCache) && _useExternalLocationService) {

            try {
                using var httpClient = new HttpClient();
                const string userAgent = "VpnHood-Client";
                var providers = new List<IIpLocationProvider> {
                    new CloudflareLocationProvider(httpClient, userAgent),
                    new IpLocationIoProvider(httpClient, userAgent, apiKey: null)
                };
                if (_useInternalLocationService)
                    providers.Add(IpRangeLocationProvider);

                var compositeProvider = new CompositeIpLocationProvider(VhLogger.Instance, providers);
                var ipLocation = await compositeProvider.GetCurrentLocation(cancellationToken).VhConfigureAwait();
                UpdateCurrentCountry(ipLocation.CountryCode);
            }
            catch (Exception ex) {
                ReportError(ex, "Could not find country code.");
            }
        }

        // return last country
        _isFindingCountryCode = false;
        return _appPersistState.ClientCountryCode ?? RegionInfo.CurrentRegion.Name;
    }

    private void Client_StateChanged(object sender, EventArgs e)
    {
        // do not disconnect by this event when _isConnecting is set. More than one client may create, and they may fire dispose state
        // during operation, so we should not disconnect the app connection before they finish their job
        if (_clientManager?.ConnectionInfo?.ClientState == ClientState.Disposed && !_isConnecting) {
            VhLogger.Instance.LogInformation("Disconnecting due to the client disposal.");
            _ = Disconnect();
            return;
        }

        FireConnectionStateChanged();
    }


    private readonly AsyncLock _disconnectLock = new();

    public async Task Disconnect(bool byUser = false)
    {
        using var lockAsync = await _disconnectLock.LockAsync().VhConfigureAwait();
        if (_isDisconnecting || _hasDisconnectRequested)
            return;
        _hasDisconnectRequested = true;

        try {
            // set disconnect reason by user
            _hasDisconnectedByUser = byUser;
            VhLogger.Instance.LogInformation(byUser
                ? "User has requested a disconnection."
                : "App has requested a disconnection.");

            // change state to disconnecting
            _isDisconnecting = true;
            FireConnectionStateChanged();

            // check diagnose
            if (_hasDiagnoseRequested && _appPersistState.LastError == null)
                _appPersistState.LastError =
                    new ApiError(new Exception("Diagnoser has finished and no issue has been detected."));

            // cancel current connecting if any
            _connectCts?.Cancel();

            // close client
            // do not wait for bye if user request disconnection
            if (_clientManager != null) {
                _clientManager.StateChanged -= Client_StateChanged;
                _ = _clientManager.DisposeAsync().VhConfigureAwait();
            }

            LogService.Stop();
        }
        catch (Exception ex) {
            ReportError(ex, "Error in disconnecting.");
        }
        finally {
            _appPersistState.LastError ??= LastConnectionInfo?.Error;
            _activeClientProfileId = null;
            _lastConnectionInfo = _clientManager?.ConnectionInfo;
            _isConnecting = false;
            _isDisconnecting = false;
            _requestedServerLocation = null;
            _clientManager = null;
            ConnectedTime = null;
            FireConnectionStateChanged();
        }
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
    public async Task VersionCheck(bool force = false)
    {
        using var lockAsync = await _versionCheckLock.LockAsync().VhConfigureAwait();
        if (!force && _appPersistState.UpdateIgnoreTime + _versionCheckInterval > DateTime.Now)
            return;

        // check version by app container
        try {
            if (ActiveUiContext.Context != null && Services.UpdaterProvider != null &&
                await Services.UpdaterProvider.Update(ActiveUiContext.RequiredContext).VhConfigureAwait()) {
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

            VhLogger.Instance.LogTrace("Retrieving the latest publish info...");

            using var httpClient = new HttpClient();
            var publishInfoJson = await httpClient.GetStringAsync(Features.UpdateInfoUrl).VhConfigureAwait();
            var latestPublishInfo = VhUtil.JsonDeserialize<PublishInfo>(publishInfoJson);
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
        // calculate packetCaptureIpRanges
        var ipRanges = IpNetwork.All.ToIpRanges();
        if (UserSettings.UseAppIpFilter) {
            ipRanges = ipRanges.Intersect(IpFilterParser.ParseIncludes(SettingsService.IpFilterSettings.AppIpFilterIncludes));
            ipRanges = ipRanges.Exclude(IpFilterParser.ParseExcludes(SettingsService.IpFilterSettings.AppIpFilterExcludes));
        }

        // exclude client country IPs
        if (UserSettings.TunnelClientCountry)
            return ipRanges;

        try {
            _isLoadingCountryIpRange = true;
            FireConnectionStateChanged();

            if (!_useInternalLocationService)
                throw new InvalidOperationException("Could not use internal location service because it is disabled.");

            var countryCode = await GetCurrentCountryAsync(true, cancellationToken).VhConfigureAwait();
            var countryIpRanges = await IpRangeLocationProvider.GetIpRanges(countryCode).VhConfigureAwait();
            VhLogger.Instance.LogInformation("Client CountryCode is: {CountryCode}", VhUtil.TryGetCountryName(_appPersistState.ClientCountryCode));
            ipRanges = ipRanges.Exclude(countryIpRanges);
        }
        catch (Exception ex) {
            ReportError(ex, "Could not get ip locations of your country.");
            if (!UserSettings.TunnelClientCountry) {
                UserSettings.TunnelClientCountry = true;
                Settings.Save();
            }
        }

        finally {
            _isLoadingCountryIpRange = false;
        }

        return ipRanges;
    }

    public async Task ExtendByRewardedAd(CancellationToken cancellationToken)
    {
        // save variable to prevent null reference exception
        var clientManager = _clientManager;
        var connectionInfo = clientManager?.ConnectionInfo;

        if (Services.AdService.CanShowRewarded != true)
            throw new InvalidOperationException("Rewarded ad is not supported in this app.");

        if (clientManager == null || connectionInfo?.SessionInfo == null ||
            connectionInfo is not { ClientState: ClientState.Connected })
            throw new InvalidOperationException("Could not show ad. The VPN is not connected.");

        if (connectionInfo.SessionStatus?.CanExtendByRewardedAd != true)
            throw new InvalidOperationException("Could not extend this session by a rewarded ad at this time.");

        // use client manager so it can exclude ad data from the session
        var adRequest = new AdRequest {
            AdRequestType = AdRequestType.Rewarded,
            SessionId = connectionInfo.SessionInfo.SessionId,
            RequestId = Guid.NewGuid()
        };
        await clientManager.SendAdRequest(adRequest, cancellationToken);
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
}