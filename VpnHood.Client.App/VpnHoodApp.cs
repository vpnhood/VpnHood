using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.Exceptions;
using VpnHood.Client.App.Settings;
using VpnHood.Client.Device;
using VpnHood.Client.Diagnosing;
using VpnHood.Common;
using VpnHood.Common.Exceptions;
using VpnHood.Common.JobController;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Client.App;

public class VpnHoodApp : Singleton<VpnHoodApp>, IAsyncDisposable, IIpRangeProvider, IJob
{
    private const string FileNameLog = "log.txt";
    private const string FileNameSettings = "settings.json";
    private const string FolderNameProfiles = "profiles";
    private readonly SocketFactory? _socketFactory;
    private readonly bool _loadCountryIpGroups;
    private readonly string? _appGa4MeasurementId;
    private bool _hasAnyDataArrived;
    private bool _hasConnectRequested;
    private bool _hasDiagnoseStarted;
    private bool _hasDisconnectedByUser;
    private DateTime? _connectRequestTime;
    private IpGroupManager? _ipGroupManager;
    private bool _isConnecting;
    private bool _isDisconnecting;
    private SessionStatus? _lastSessionStatus;
    private string? _lastError;
    private IpGroup? _lastCountryIpGroup;
    private AppConnectionState _lastConnectionState;
    private int _initializingState;
    private readonly TimeSpan _versionCheckInterval;
    private VpnHoodClient? Client => ClientConnect?.Client;
    private SessionStatus? LastSessionStatus => Client?.SessionStatus ?? _lastSessionStatus;
    private string TempFolderPath => Path.Combine(AppDataFolderPath, "Temp");
    private string IpGroupsFolderPath => Path.Combine(TempFolderPath, "ipgroups");
    private VersionStatus _versionStatus = VersionStatus.Unknown;
    private CancellationTokenSource? _connectCts;
    private DateTime? _connectedTime;

    public bool VersionCheckRequired { get; private set; }
    public event EventHandler? ConnectionStateChanged;
    public bool IsWaitingForAd { get; private set; }
    public bool IsIdle => ConnectionState == AppConnectionState.None;
    public VpnHoodConnect? ClientConnect { get; private set; }
    public TimeSpan SessionTimeout { get; set; }
    public Diagnoser Diagnoser { get; set; } = new();
    public ClientProfile? ActiveClientProfile { get; private set; }
    public Guid LastActiveClientProfileId { get; private set; }
    public string AppDataFolderPath { get; }
    public AppSettings Settings { get; }
    public UserSettings UserSettings => Settings.UserSettings;
    public AppFeatures Features { get; }
    public ClientProfileService ClientProfileService { get; }
    public IDevice Device { get; }
    public PublishInfo? LatestPublishInfo { get; private set; }
    public JobSection JobSection { get; }
    public TimeSpan TcpTimeout { get; set; } = new ClientOptions().ConnectTimeout;
    public AppLogService LogService { get; }
    public AppResources Resources { get; }
    public IAppAccountService? AccountService { get; set; }
    public IAppUpdaterService? AppUpdaterService { get; set; }
    public IAppAdService? AppAdService { get; set; }
    private VpnHoodApp(IDevice device, AppOptions? options = default)
    {
        options ??= new AppOptions();
#pragma warning disable CS0618 // Type or member is obsolete
        MigrateUserDataFromXamarin(options.AppDataFolderPath); // Deprecated >= 400
#pragma warning restore CS0618 // Type or member is obsolete
        Directory.CreateDirectory(options.AppDataFolderPath); //make sure directory exists
        Resources = options.Resources;

        Device = device;
        device.OnStartAsService += Device_OnStartAsService;

        AppDataFolderPath = options.AppDataFolderPath ?? throw new ArgumentNullException(nameof(options.AppDataFolderPath));
        Settings = AppSettings.Load(Path.Combine(AppDataFolderPath, FileNameSettings));
        Settings.OnSaved += Settings_OnSaved;
        ClientProfileService = new ClientProfileService(Path.Combine(AppDataFolderPath, FolderNameProfiles));
        SessionTimeout = options.SessionTimeout;
        _socketFactory = options.SocketFactory;
        Diagnoser.StateChanged += (_, _) => CheckConnectionStateChanged();
        LogService = new AppLogService(Path.Combine(AppDataFolderPath, FileNameLog));
        _loadCountryIpGroups = options.LoadCountryIpGroups;
        _appGa4MeasurementId = options.AppGa4MeasurementId;
        _versionCheckInterval = options.VersionCheckInterval;

        // configure update job section
        JobSection = new JobSection(new JobOptions
        {
            Interval = options.VersionCheckInterval,
            DueTime = options.VersionCheckInterval > TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : options.VersionCheckInterval,
            Name = "VersionCheck"
        });

        // create start up logger
        if (!device.IsLogToConsoleSupported) UserSettings.Logging.LogToConsole = false;
        LogService.Start(Settings.UserSettings.Logging, false);

        // add default test public server if not added yet
        RemoveClientProfileByTokenId("1047359c-a107-4e49-8425-c004c41ffb8f"); // old one; deprecated in version v2.0.261 and upper
        if (Settings.TestServerTokenAutoAdded != Settings.PublicAccessKey)
        {
            ClientProfileService.ImportAccessKey(Settings.PublicAccessKey);
            Settings.TestServerTokenAutoAdded = Settings.PublicAccessKey;
        }

        // initialize features
        Features = new AppFeatures
        {
            Version = typeof(VpnHoodApp).Assembly.GetName().Version,
            TestServerTokenId = Token.FromAccessKey(Settings.PublicAccessKey).TokenId,
            IsExcludeAppsSupported = Device.IsExcludeAppsSupported,
            IsIncludeAppsSupported = Device.IsIncludeAppsSupported,
            UpdateInfoUrl = options.UpdateInfoUrl,
            UiName = options.UiName,
            IsAddServerSupported = options.IsAddServerSupported,
        };

        JobRunner.Default.Add(this);
    }

    public AppState State
    {
        get
        {
            var connectionState = ConnectionState;
            return new AppState
            {
                ConfigTime = Settings.ConfigTime,
                ConnectionState = connectionState,
                IsIdle = IsIdle,
                CanConnect = connectionState is AppConnectionState.None,
                CanDisconnect = !_isDisconnecting && (connectionState 
                    is AppConnectionState.Connected or AppConnectionState.Connecting
                    or AppConnectionState.Diagnosing or AppConnectionState.Waiting),
                ActiveClientProfileId = ActiveClientProfile?.ClientProfileId,
                LastActiveClientProfileId = LastActiveClientProfileId,
                LogExists = IsIdle && File.Exists(LogService.LogFilePath),
                LastError = _lastError,
                HasDiagnoseStarted = _hasDiagnoseStarted,
                HasDisconnectedByUser = _hasDisconnectedByUser,
                HasProblemDetected = _hasConnectRequested && IsIdle && (_hasDiagnoseStarted || _lastError != null),
                SessionStatus = LastSessionStatus,
                Speed = Client?.Stat.Speed ?? new Traffic(),
                AccountTraffic = Client?.Stat.AccountTraffic ?? new Traffic(),
                SessionTraffic = Client?.Stat.SessionTraffic ?? new Traffic(),
                ClientIpGroup = _lastCountryIpGroup,
                IsWaitingForAd = IsWaitingForAd,
                VersionStatus = _versionStatus,
                LastPublishInfo =
                    _versionStatus is VersionStatus.Deprecated or VersionStatus.Old ? LatestPublishInfo : null,
                ConnectRequestTime = _connectRequestTime,
                IsUdpChannelSupported = Client?.Stat.IsUdpChannelSupported
            };
        }
    }

    public AppConnectionState ConnectionState
    {
        get
        {
            if (_initializingState > 0) return AppConnectionState.Initializing;
            if (Diagnoser.IsWorking) return AppConnectionState.Diagnosing;
            if (_isDisconnecting || Client?.State == ClientState.Disconnecting) return AppConnectionState.Disconnecting;
            if (_isConnecting || Client?.State == ClientState.Connecting) return AppConnectionState.Connecting;
            if (Client?.State == ClientState.Connected && IsWaitingForAd) return AppConnectionState.Connecting;
            if (Client?.State == ClientState.Connected) return AppConnectionState.Connected;
            if (ClientConnect?.IsWaiting == true) return AppConnectionState.Waiting;
            return AppConnectionState.None;
        }
    }

    private void CheckConnectionStateChanged()
    {
        // check changed state
        var connectionState = ConnectionState;
        if (connectionState == _lastConnectionState)
            return;
        _lastConnectionState = connectionState;
        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await Disconnect();
        Device.Dispose();
        DisposeSingleton();
    }

    public event EventHandler? ClientConnectCreated;

    public static VpnHoodApp Init(IDevice device, AppOptions? options = default)
    {
        return new VpnHoodApp(device, options);
    }

    private void RemoveClientProfileByTokenId(string tokenId)
    {
        var clientProfile = ClientProfileService.FindByTokenId(tokenId);
        if (clientProfile != null)
            ClientProfileService.Remove(clientProfile.ClientProfileId);
    }

    private void Device_OnStartAsService(object sender, EventArgs e)
    {
        var clientProfile =
            GetDefaultClientProfile()
            ?? throw new Exception("There is no default profile.");

        _ = Connect(clientProfile.ClientProfileId);
    }

    public void ClearLastError()
    {
        if (!IsIdle)
            return; //can just set in Idle State

        _lastError = null;
        _hasDiagnoseStarted = false;
        _hasDisconnectedByUser = false;
    }

    public async Task Connect(Guid? clientProfileId = null, bool diagnose = false, 
        string? userAgent = default, bool throwException = true, CancellationToken cancellationToken = default)
    {
        // set default profileId to clientProfileId if not set
        clientProfileId ??= GetDefaultClientProfile()?.ClientProfileId;
        if (clientProfileId == null) throw new NotExistsException("Could not find any VPN profile to connect.");

        // disconnect if user request diagnosing
        if (ActiveClientProfile != null && ActiveClientProfile.ClientProfileId != clientProfileId ||
            !IsIdle && diagnose && !_hasDiagnoseStarted)
            await Disconnect(true);

        // check already in progress
        if (ActiveClientProfile != null || !IsIdle)
        {
            var ex = new InvalidOperationException("Connection is already in progress!");
            VhLogger.Instance.LogError(ex.Message);
            throw ex;
        }

        try
        {
            // prepare logger
            ClearLastError();
            _isConnecting = true;
            _hasAnyDataArrived = false;
            _hasDisconnectedByUser = false;
            _hasConnectRequested = true;
            IsWaitingForAd = false;
            _hasDiagnoseStarted = diagnose;
            _connectRequestTime = DateTime.Now;
            CheckConnectionStateChanged();
            LogService.Start(Settings.UserSettings.Logging, diagnose);

            VhLogger.Instance.LogInformation("VpnHood Client is Connecting ...");

            // create cancellationToken
            _connectCts = new CancellationTokenSource();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _connectCts.Token);
            cancellationToken = linkedCts.Token;

            // Set ActiveProfile
            ActiveClientProfile = ClientProfileService.Get(clientProfileId.Value);
            LastActiveClientProfileId = ActiveClientProfile.ClientProfileId;

            // create packet capture
            var packetCapture = await Device.CreatePacketCapture();
            if (packetCapture.IsMtuSupported)
                packetCapture.Mtu = TunnelDefaults.MtuWithoutFragmentation;

            // App filters
            if (packetCapture.CanExcludeApps && UserSettings.AppFiltersMode == FilterMode.Exclude)
                packetCapture.ExcludeApps = UserSettings.AppFilters;

            if (packetCapture.CanIncludeApps && UserSettings.AppFiltersMode == FilterMode.Include)
                packetCapture.IncludeApps = UserSettings.AppFilters;

            // connect
            await ConnectInternal(packetCapture, ActiveClientProfile.Token, userAgent, cancellationToken);
        }
        catch (Exception ex)
        {
            //user may disconnect before connection closed
            if (!_hasDisconnectedByUser)
            {
                VhLogger.Instance.LogError(ex.Message);
                _lastError = ex.Message;
            }

            await Disconnect();

            if (throwException)
                throw;
        }
        finally
        {
            _connectCts?.Dispose();
            _connectCts = null;
            _isConnecting = false;
            IsWaitingForAd = false;
            CheckConnectionStateChanged();
        }
    }

    private void Settings_OnSaved(object sender, EventArgs e)
    {
        if (Client == null) return;
        Client.UseUdpChannel = UserSettings.UseUdpChannel;
        Client.DropUdpPackets = UserSettings.DropUdpPackets;
    }

    private async Task<string?> GetClientCountry()
    {
        try
        {
            var ipAddress =
                await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetwork) ??
                await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetworkV6);

            if (ipAddress == null)
                return null;

            var ipGroupManager = await GetIpGroupManager();
            _lastCountryIpGroup = await ipGroupManager.FindIpGroup(ipAddress, Settings.LastCountryIpGroupId);
            Settings.LastCountryIpGroupId = _lastCountryIpGroup?.IpGroupId;
            return _lastCountryIpGroup?.IpGroupName;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not retrieve client country from public ip services.");
            return null;
        }
    }

    private async Task ConnectInternal(IPacketCapture packetCapture, Token token, string? userAgent, CancellationToken cancellationToken)
    {
        packetCapture.OnStopped += PacketCapture_OnStopped;

        // log general info
        VhLogger.Instance.LogInformation($"AppVersion: {GetType().Assembly.GetName().Version}");
        VhLogger.Instance.LogInformation($"Time: {DateTime.UtcNow.ToString("u", new CultureInfo("en-US"))}");
        VhLogger.Instance.LogInformation($"OS: {Device.OsInfo}");
        VhLogger.Instance.LogInformation($"UserAgent: {userAgent}");

        // it slows down tests and does not need to be logged in normal situation
        if (_hasDiagnoseStarted)
            VhLogger.Instance.LogInformation($"Country: {await GetClientCountry()}");

        // show token info
        VhLogger.Instance.LogInformation($"TokenId: {VhLogger.FormatId(token.TokenId)}, SupportId: {VhLogger.FormatId(token.SupportId)}");

        // dump user settings
        VhLogger.Instance.LogInformation("UserSettings: {UserSettings}",
            JsonSerializer.Serialize(UserSettings, new JsonSerializerOptions { WriteIndented = true }));

        // save settings
        Settings.Save();

        // Show ad if required
        //todo: add test
        string? adData = null;
        if (token.IsAdRequired)
        {
            if (AppAdService == null) throw new Exception("This server requires a display ad, but AppAdService has not been initialized.");
            IsWaitingForAd = true;
            adData = await AppAdService.ShowAd(cancellationToken) ?? throw new AdException("This server requires a display ad but could not display it.");
            IsWaitingForAd = false;
        }

        // calculate packetCaptureIpRanges
        var packetCaptureIpRanges = IpNetwork.All.ToIpRanges();
        if (!VhUtil.IsNullOrEmpty(UserSettings.PacketCaptureIncludeIpRanges))
            packetCaptureIpRanges = packetCaptureIpRanges.Intersect(UserSettings.PacketCaptureIncludeIpRanges);
        if (!VhUtil.IsNullOrEmpty(UserSettings.PacketCaptureExcludeIpRanges))
            packetCaptureIpRanges = packetCaptureIpRanges.Exclude(UserSettings.PacketCaptureExcludeIpRanges);

        // create clientOptions
        var clientOptions = new ClientOptions
        {
            SessionTimeout = SessionTimeout,
            ExcludeLocalNetwork = UserSettings.ExcludeLocalNetwork,
            IpRangeProvider = this,
            PacketCaptureIncludeIpRanges = packetCaptureIpRanges.ToArray(),
            MaxDatagramChannelCount = UserSettings.MaxDatagramChannelCount,
            ConnectTimeout = TcpTimeout,
            AllowAnonymousTracker = UserSettings.AllowAnonymousTracker,
            DropUdpPackets = UserSettings.DropUdpPackets,
            AppGa4MeasurementId = _appGa4MeasurementId,
            AdData = adData
        };
        if (_socketFactory != null) clientOptions.SocketFactory = _socketFactory;
        if (userAgent != null) clientOptions.UserAgent = userAgent;

        // Create Client
        var clientConnect = new VpnHoodConnect(
            packetCapture,
            Settings.ClientId,
            token,
            clientOptions,
            new ConnectOptions
            {
                MaxReconnectCount = UserSettings.MaxReconnectCount,
                UdpChannelMode = UserSettings.UseUdpChannel ? UdpChannelMode.On : UdpChannelMode.Off
            });

        ClientConnectCreated?.Invoke(this, EventArgs.Empty);
        clientConnect.StateChanged += ClientConnect_StateChanged;
        ClientConnect = clientConnect; // set here to allow disconnection

        try
        {
            if (_hasDiagnoseStarted)
                await Diagnoser.Diagnose(clientConnect, cancellationToken);
            else
                await Diagnoser.Connect(clientConnect, cancellationToken);

            // set connected time
            _connectedTime = DateTime.Now;

            // update access token if ResponseAccessKey is set
            if (clientConnect.Client.ResponseAccessKey != null)
                ClientProfileService.UpdateTokenByAccessKey(token, clientConnect.Client.ResponseAccessKey);

            // check version after first connection
            _ = VersionCheck();
        }
        finally
        {

            // try to update token from url after connection or error if ResponseAccessKey is not set
            if (clientConnect.Client.ResponseAccessKey == null && !string.IsNullOrEmpty(token.ServerToken.Url))
                _ = ClientProfileService.UpdateTokenFromUrl(token); 
        }
    }

    private void ClientConnect_StateChanged(object sender, EventArgs e)
    {
        if (ClientConnect?.IsDisposed == true)
            _ = Disconnect();
        else
            CheckConnectionStateChanged();
    }

    private async Task<IpRange[]?> GetIncludeIpRanges(FilterMode filterMode, string[]? ipGroupIds)
    {
        if (filterMode == FilterMode.All || VhUtil.IsNullOrEmpty(ipGroupIds))
            return null;

        if (filterMode == FilterMode.Include)
            return await GetIpRanges(ipGroupIds);

        return IpRange.Invert(await GetIpRanges(ipGroupIds)).ToArray();
    }

    private async Task<IpRange[]> GetIpRanges(IEnumerable<string> ipGroupIds)
    {
        var ipRanges = new List<IpRange>();
        foreach (var ipGroupId in ipGroupIds)
            try
            {
                if (ipGroupId.Equals("custom", StringComparison.OrdinalIgnoreCase))
                    ipRanges.AddRange(UserSettings.CustomIpRanges ?? Array.Empty<IpRange>());
                else
                {
                    var ipGroupManager = await GetIpGroupManager();
                    ipRanges.AddRange(await ipGroupManager.GetIpRanges(ipGroupId));
                }
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, $"Could not add {nameof(IpRange)} of Group {ipGroupId}");
            }

        return IpRange.Sort(ipRanges).ToArray();
    }

    private void PacketCapture_OnStopped(object sender, EventArgs e)
    {
        _ = Disconnect();
    }

    public async Task Disconnect(bool byUser = false)
    {
        if (_isDisconnecting)
            return;

        try
        {
            if (byUser)
            {
                VhLogger.Instance.LogTrace("User requests disconnection.");
                _hasDisconnectedByUser = true;
            }

            // save settings
            Settings.Save();

            _isDisconnecting = true;
            CheckConnectionStateChanged();

            // check for any success
            if (Client != null && _connectedTime!=null)
            {
                _hasAnyDataArrived = Client.Stat.SessionTraffic.Received > 1000;
                if (_lastError == null && !_hasAnyDataArrived && UserSettings is { IpGroupFiltersMode: FilterMode.All, TunnelClientCountry: true })
                    _lastError = "No data has been received.";
            }

            // check diagnose
            if (_hasDiagnoseStarted && _lastError == null)
                _lastError = "Diagnoser has finished and no issue has been detected.";

            // close client
            try
            {
                // cancel current connecting if any
                _connectCts?.Cancel();

                // do not wait for bye if user request disconnection
                if (ClientConnect != null)
                    await ClientConnect.DisposeAsync(waitForBye: !byUser);
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(GeneralEventId.Session, ex, "Could not dispose the client properly.");
            }

            LogService.Stop();
        }
        finally
        {
            _lastError ??= LastSessionStatus?.ErrorMessage;
            ActiveClientProfile = null;
            _lastSessionStatus = Client?.SessionStatus;
            _isConnecting = false;
            _isDisconnecting = false;
            _connectedTime = null;
            ClientConnect = null;
            CheckConnectionStateChanged();
        }
    }

    public async Task<IpGroup[]> GetIpGroups()
    {
        var ipGroupManager = await GetIpGroupManager();
        return await ipGroupManager.GetIpGroups();
    }

    private async Task<IpGroupManager> GetIpGroupManager()
    {
        if (_ipGroupManager != null)
            return _ipGroupManager;

        _ipGroupManager = await IpGroupManager.Create(IpGroupsFolderPath);

        // AddFromIp2Location if hash has been changed
        if (_loadCountryIpGroups)
        {
            try
            {
                _initializingState++;
                CheckConnectionStateChanged();
                await using var memZipStream = new MemoryStream(Resource.IP2LOCATION_LITE_DB1_IPV6_CSV);
                using var zipArchive = new ZipArchive(memZipStream);
                var entry = zipArchive.GetEntry("IP2LOCATION-LITE-DB1.IPV6.CSV") ?? throw new Exception("Could not find ip2location database.");
                await _ipGroupManager.InitByIp2LocationZipStream(entry);
            }
            finally
            {
                _initializingState--;
                CheckConnectionStateChanged();
            }
        }

        return _ipGroupManager;
    }

    public void VersionCheckPostpone()
    {
        Settings.LastUpdateCheckTime = DateTime.Now;
        VersionCheckRequired = false;
        _versionStatus = VersionStatus.Unknown;
        Settings.Save();
    }

    public async Task VersionCheck(bool force = false)
    {
        if (!force && Settings.LastUpdateCheckTime != null && Settings.LastUpdateCheckTime.Value + _versionCheckInterval > DateTime.Now)
            return;

        // check version by app container
        if (AppUpdaterService != null)
        {
            try
            {
                if (await AppUpdaterService.Update())
                {
                    _versionStatus = VersionStatus.Unknown; // version status is unknown when app container can do it
                    VersionCheckRequired = false;
                    Settings.LastUpdateCheckTime = DateTime.Now;
                    Settings.Save();
                    return;
                }
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogWarning(ex, "Could not check version by VersionCheckProc.");
            }
        }

        // app container should check version if possible regardless of the result of VersionCheckByUpdateInfo
        VersionCheckRequired = true;

        // check version by update info
        if (await VersionCheckByUpdateInfo())
        {
            Settings.LastUpdateCheckTime = DateTime.Now;
            Settings.Save();
        }
    }

    private async Task<bool> VersionCheckByUpdateInfo()
    {
        try
        {
            if (Features.UpdateInfoUrl == null)
                return true; // no update info url. Job done

            VhLogger.Instance.LogTrace("Retrieving the latest publish info...");

            using var httpClient = new HttpClient();
            var publishInfoJson = await httpClient.GetStringAsync(Features.UpdateInfoUrl);
            LatestPublishInfo = VhUtil.JsonDeserialize<PublishInfo>(publishInfoJson);

            // Check version
            if (LatestPublishInfo.Version == null)
                throw new Exception("Version is not available in publish info.");

            // set default notification delay
            if (Features.Version <= LatestPublishInfo.DeprecatedVersion)
                _versionStatus = VersionStatus.Deprecated;

            else if (Features.Version < LatestPublishInfo.Version &&
                     DateTime.UtcNow - LatestPublishInfo.ReleaseDate > LatestPublishInfo.NotificationDelay)
                _versionStatus = VersionStatus.Old;

            else
                _versionStatus = VersionStatus.Latest;

            VhLogger.Instance.LogInformation("The latest publish info has been retrieved. VersionStatus: {VersionStatus}, LatestVersion: {LatestVersion}",
                _versionStatus, LatestPublishInfo.Version);

            return true; // Job done
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning(ex, "Could not retrieve the latest publish info information.");
            return false; // could not retrieve the latest publish info. try later
        }
    }

    public async Task<IpRange[]?> GetIncludeIpRanges(IPAddress clientIp)
    {
        var ipGroupManager = await GetIpGroupManager();
        _lastCountryIpGroup = await ipGroupManager.FindIpGroup(clientIp, Settings.LastCountryIpGroupId);
        Settings.LastCountryIpGroupId = _lastCountryIpGroup?.IpGroupId;
        VhLogger.Instance.LogInformation($"Client Country is: {_lastCountryIpGroup?.IpGroupName}");

        // use TunnelMyCountry
        if (!UserSettings.TunnelClientCountry)
            return _lastCountryIpGroup != null ? await GetIncludeIpRanges(FilterMode.Exclude, [_lastCountryIpGroup.IpGroupId]) : null;

        // use advanced options
        return await GetIncludeIpRanges(UserSettings.IpGroupFiltersMode, UserSettings.IpGroupFilters);
    }

    public Task RunJob()
    {
        return VersionCheck();
    }

    public ClientProfile? GetDefaultClientProfile()
    {
        // find default
        var clientProfile = ClientProfileService.FindById(Settings.UserSettings.DefaultClientProfileId ?? Guid.Empty);
        if (clientProfile != null)
            return clientProfile;

        // find first
        clientProfile = ClientProfileService.List().FirstOrDefault();
        if (clientProfile == null)
            return null;

        // set first as default
        Settings.UserSettings.DefaultClientProfileId = clientProfile.ClientProfileId;
        Settings.Save();
        return clientProfile;
    }

    public ClientProfile? GetActiveClientProfile()
    {
        return IsIdle
            ? null
            : ClientProfileService.FindById(LastActiveClientProfileId);
    }

    [Obsolete("Deprecated >= 400", false)]
    private static void MigrateUserDataFromXamarin(string folderPath)
    {
        try
        {
            var oldPath = Path.Combine(Path.GetDirectoryName(folderPath)!, ".local", "share", "VpnHood");
            if (Directory.Exists(oldPath) && !Directory.Exists(folderPath))
                Directory.Move(oldPath, folderPath);
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not migrate user data from Xamarin.");
        }
    }

}