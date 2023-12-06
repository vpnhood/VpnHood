using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Settings;
using VpnHood.Client.Device;
using VpnHood.Client.Diagnosing;
using VpnHood.Common;
using VpnHood.Common.JobController;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Client.App;

public class VpnHoodApp : IAsyncDisposable, IIpRangeProvider, IJob
{
    private const string FileNameLog = "log.txt";
    private const string FileNameSettings = "settings.json";
    private const string FolderNameProfileStore = "profiles";
    private static VpnHoodApp? _instance;
    private readonly IAppProvider _appProvider;
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
    private VpnHoodClient? Client => ClientConnect?.Client;
    private SessionStatus? LastSessionStatus => Client?.SessionStatus ?? _lastSessionStatus;
    private string TempFolderPath => Path.Combine(AppDataFolderPath, "Temp");
    private string IpGroupsFolderPath => Path.Combine(TempFolderPath, "ipgroups");

    public VersionStatus VersionStatus { get; private set; } = VersionStatus.Unknown;

    public event EventHandler? ConnectionStateChanged;
    public bool IsWaitingForAd { get; set; }
    public bool IsIdle => ConnectionState == AppConnectionState.None;
    public VpnHoodConnect? ClientConnect { get; private set; }
    public TimeSpan SessionTimeout { get; set; }
    public Diagnoser Diagnoser { get; set; } = new();
    public ClientProfile? ActiveClientProfile { get; private set; }
    public Guid LastActiveClientProfileId { get; private set; }
    public static VpnHoodApp Instance => _instance ?? throw new InvalidOperationException($"{nameof(VpnHoodApp)} has not been initialized yet!");
    public static bool IsInit => _instance != null;
    public string AppDataFolderPath { get; }
    public AppSettings Settings { get; }
    public UserSettings UserSettings => Settings.UserSettings;
    public AppFeatures Features { get; }
    public ClientProfileStore ClientProfileStore { get; }
    public IDevice Device => _appProvider.Device;
    public PublishInfo? LatestPublishInfo { get; private set; }
    public JobSection JobSection { get; }
    public TimeSpan TcpTimeout { get; set; } = new ClientOptions().ConnectTimeout;
    public AppLogService LogService { get; }
    public AppResources Resources { get; }

    private VpnHoodApp(IAppProvider appProvider, AppOptions? options = default)
    {
        if (IsInit) throw new InvalidOperationException("VpnHoodApp is already initialized.");
        options ??= new AppOptions();
        MigrateUserDataFromXamarin(options.AppDataFolderPath); // Deprecated >= 400
        Directory.CreateDirectory(options.AppDataFolderPath); //make sure directory exists
        Resources = options.Resources;

        _appProvider = appProvider ?? throw new ArgumentNullException(nameof(appProvider));
        if (_appProvider.Device == null) throw new ArgumentNullException(nameof(_appProvider.Device));
        Device.OnStartAsService += Device_OnStartAsService;

        AppDataFolderPath = options.AppDataFolderPath ?? throw new ArgumentNullException(nameof(options.AppDataFolderPath));
        Settings = AppSettings.Load(Path.Combine(AppDataFolderPath, FileNameSettings));
        Settings.OnSaved += Settings_OnSaved;
        ClientProfileStore = new ClientProfileStore(Path.Combine(AppDataFolderPath, FolderNameProfileStore));
        SessionTimeout = options.SessionTimeout;
        _socketFactory = options.SocketFactory;
        Diagnoser.StateChanged += (_, _) => CheckConnectionStateChanged();
        JobSection = new JobSection(options.UpdateCheckerInterval);
        LogService = new AppLogService(Path.Combine(AppDataFolderPath, FileNameLog));
        _loadCountryIpGroups = options.LoadCountryIpGroups;
        _appGa4MeasurementId = options.AppGa4MeasurementId;

        // create start up logger
        if (!appProvider.IsLogToConsoleSupported) UserSettings.Logging.LogToConsole = false;
        LogService.Start(Settings.UserSettings.Logging, false);

        // add default test public server if not added yet
        RemoveClientProfileByTokenId(Guid.Parse("1047359c-a107-4e49-8425-c004c41ffb8f")); //old one; deprecated in version v2.0.261 and upper
        if (Settings.TestServerTokenAutoAdded != Settings.TestServerAccessKey)
        {
            ClientProfileStore.AddAccessKey(Settings.TestServerAccessKey);
            Settings.TestServerTokenAutoAdded = Settings.TestServerAccessKey;
        }

        // Set default ClientId if not exists
        if (ClientProfileStore.ClientProfileItems.All(x => x.ClientProfile.ClientProfileId != Settings.UserSettings.DefaultClientProfileId))
        {
            Settings.UserSettings.DefaultClientProfileId = ClientProfileStore.ClientProfileItems.FirstOrDefault()?.ClientProfile.ClientProfileId;
            Settings.Save();
        }

        // initialize features
        Features = new AppFeatures
        {
            Version = typeof(VpnHoodApp).Assembly.GetName().Version,
            TestServerTokenId = Token.FromAccessKey(Settings.TestServerAccessKey).TokenId,
            IsExcludeAppsSupported = Device.IsExcludeAppsSupported,
            IsIncludeAppsSupported = Device.IsIncludeAppsSupported,
            UpdateInfoUrl = options.UpdateInfoUrl,
        };
        _ = CheckNewVersion();

        _instance = this;
        JobRunner.Default.Add(this);
    }

    public AppState State => new()
    {
        ConfigTime = Settings.ConfigTime,
        ConnectionState = ConnectionState,
        IsIdle = IsIdle,
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
        VersionStatus = VersionStatus,
        LastPublishInfo = VersionStatus is VersionStatus.Deprecated or VersionStatus.Old ? LatestPublishInfo : null,
        ConnectRequestTime = _connectRequestTime
    };

    public AppConnectionState ConnectionState
    {
        get
        {
            if (_initializingState > 0 ) return AppConnectionState.Initializing;
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
        var connectionState = ConnectionState;
        if (connectionState == _lastConnectionState)
            return;
        _lastConnectionState = connectionState;

        // Check new version after connection
        if (VersionStatus == VersionStatus.Unknown)
            _ = CheckNewVersion();

        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        if (_instance == null) return;
        await Disconnect();
        _instance = null;
    }

    public event EventHandler? ClientConnectCreated;

    public static VpnHoodApp Init(IAppProvider clientAppProvider, AppOptions? options = default)
    {
        return new VpnHoodApp(clientAppProvider, options);
    }

    private void RemoveClientProfileByTokenId(Guid guid)
    {
        var clientProfile = ClientProfileStore.ClientProfiles.FirstOrDefault(x => x.TokenId == guid);
        if (clientProfile == null) return;
        ClientProfileStore.RemoveClientProfile(clientProfile.ClientProfileId);
    }

    private void Device_OnStartAsService(object sender, EventArgs e)
    {
        var clientProfile =
            (ClientProfileStore.ClientProfiles.FirstOrDefault(x => x.ClientProfileId == UserSettings.DefaultClientProfileId)
            ?? ClientProfileStore.ClientProfiles.FirstOrDefault())
            ?? throw new Exception("There is no default configuration!");

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

    public async Task Connect(Guid? clientProfileId = null, bool diagnose = false, string? userAgent = default, bool throwException = true)
    {
        // set default profileId to clientProfileId if not set
        clientProfileId ??= UserSettings.DefaultClientProfileId;

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
            _hasDiagnoseStarted = diagnose;
            _connectRequestTime = DateTime.Now;
            CheckConnectionStateChanged();
            LogService.Start(Settings.UserSettings.Logging, diagnose);

            VhLogger.Instance.LogInformation("VpnHood Client is Connecting ...");

            // Set ActiveProfile
            ActiveClientProfile = ClientProfileStore.ClientProfiles.First(x => x.ClientProfileId == clientProfileId);
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
            await ConnectInternal(packetCapture, ActiveClientProfile.TokenId, userAgent);
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
            _isConnecting = false;
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
                await IPAddressUtil.GetPublicIpAddress(System.Net.Sockets.AddressFamily.InterNetwork) ??
                await IPAddressUtil.GetPublicIpAddress(System.Net.Sockets.AddressFamily.InterNetworkV6);

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

    private async Task ConnectInternal(IPacketCapture packetCapture, Guid tokenId, string? userAgent)
    {
        packetCapture.OnStopped += PacketCapture_OnStopped;

        // log general info
        VhLogger.Instance.LogInformation($"AppVersion: {GetType().Assembly.GetName().Version}");
        VhLogger.Instance.LogInformation($"Time: {DateTime.UtcNow.ToString("u", new CultureInfo("en-US"))}");
        VhLogger.Instance.LogInformation($"OS: {Device.OperatingSystemInfo}");
        VhLogger.Instance.LogInformation($"UserAgent: {userAgent}");

        // it slows down tests and does not need to be logged in normal situation
        if (_hasDiagnoseStarted)
            VhLogger.Instance.LogInformation($"Country: {await GetClientCountry()}");

        // get token
        var token = ClientProfileStore.GetToken(tokenId, true);
        VhLogger.Instance.LogInformation($"TokenId: {VhLogger.FormatId(token.TokenId)}, SupportId: {VhLogger.FormatId(token.SupportId)}");

        // dump user settings
        VhLogger.Instance.LogInformation("UserSettings: {UserSettings}",
            JsonSerializer.Serialize(UserSettings, new JsonSerializerOptions { WriteIndented = true }));

        // save settings
        Settings.Save();

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
            AppGa4MeasurementId = _appGa4MeasurementId
        };
        if (_socketFactory != null) clientOptions.SocketFactory = _socketFactory;
        if (userAgent != null) clientOptions.UserAgent = userAgent;

        // Create Client
        ClientConnect = new VpnHoodConnect(
            packetCapture,
            Settings.ClientId,
            token,
            clientOptions,
            new ConnectOptions
            {
                MaxReconnectCount = UserSettings.MaxReconnectCount,
                UdpChannelMode = UserSettings.UseUdpChannel ? UdpChannelMode.On : UdpChannelMode.Off,
            });

        ClientConnectCreated?.Invoke(this, EventArgs.Empty);
        ClientConnect.StateChanged += ClientConnect_StateChanged;

        try
        {
            if (_hasDiagnoseStarted)
                await Diagnoser.Diagnose(ClientConnect);
            else
                await Diagnoser.Connect(ClientConnect);
        }
        finally
        {
            // update token after connection established or if error occurred
            _ = ClientProfileStore.UpdateTokenFromUrl(token);
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
            if (Client != null)
            {
                _hasAnyDataArrived = Client.Stat.SessionTraffic.Received > 1000;
                if (_lastError == null && !_hasAnyDataArrived && UserSettings is { IpGroupFiltersMode: FilterMode.All, TunnelClientCountry: true })
                    _lastError = "No data has been received.";
            }

            // check diagnose
            if (_hasDiagnoseStarted && _lastError == null)
                _lastError = "Diagnose has finished and no issue has been detected.";

            // close client
            try
            {
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

    public async Task CheckNewVersion()
    {
        if (Features.UpdateInfoUrl == null)
            return;

        try
        {
            VhLogger.Instance.LogTrace("Retrieving the latest publish info...");

            using var httpClient = new HttpClient();
            var publishInfoJson = await httpClient.GetStringAsync(Features.UpdateInfoUrl);
            LatestPublishInfo = VhUtil.JsonDeserialize<PublishInfo>(publishInfoJson);

            // Check version
            if (LatestPublishInfo.Version == null)
                throw new Exception("Version is not available in publish info.");

            // set default notification delay
            if (Features.Version <= LatestPublishInfo.DeprecatedVersion)
                VersionStatus = VersionStatus.Deprecated;

            else if (Features.Version < LatestPublishInfo.Version &&
                     DateTime.UtcNow - LatestPublishInfo.ReleaseDate > LatestPublishInfo.NotificationDelay)
                VersionStatus = VersionStatus.Old;

            else
                VersionStatus = VersionStatus.Latest;

            VhLogger.Instance.LogInformation("The latest publish info has been retrieved. VersionStatus: {VersionStatus}, LatestVersion: {LatestVersion}",
                VersionStatus, LatestPublishInfo.Version);
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning(ex, "Could not retrieve the latest publish info information.");
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
            return _lastCountryIpGroup != null ? await GetIncludeIpRanges(FilterMode.Exclude, new[] { _lastCountryIpGroup.IpGroupId }) : null;

        // use advanced options
        return await GetIncludeIpRanges(UserSettings.IpGroupFiltersMode, UserSettings.IpGroupFilters);
    }

    public Task RunJob()
    {
        VersionStatus = VersionStatus.Unknown;
        return CheckNewVersion();
    }

    public ClientProfileItem? GetDefaultClientProfile()
    {
        return ClientProfileStore.ClientProfileItems.FirstOrDefault(x => x.ClientProfile.ClientProfileId == Settings.UserSettings.DefaultClientProfileId);
    }

    public ClientProfileItem? GetActiveClientProfile()
    {
        return IsIdle
            ? null
            : ClientProfileStore.ClientProfileItems.FirstOrDefault(x => x.ClientProfile.ClientProfileId == LastActiveClientProfileId);
    }

    // Deprecated >= 400
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