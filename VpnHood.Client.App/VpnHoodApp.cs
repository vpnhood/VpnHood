using VpnHood.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using VpnHood.Tunneling;
using VpnHood.Client.Device;
using VpnHood.Client.Diagnosing;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.IO.Compression;
using VpnHood.Tunneling.Factory;
using VpnHood.Common;

namespace VpnHood.Client.App
{
    public class VpnHoodApp : IDisposable
    {
        private const string FILENAME_Log = "log.txt";
        private const string FILENAME_Settings = "settings.json";
        private const string FILENAME_IpGroups = "ipgroups.json";
        private const string FOLDERNAME_ProfileStore = "profiles";
        private readonly IAppProvider _clientAppProvider;
        private static VpnHoodApp? _instance;
        private readonly bool _logToConsole;
        private readonly SocketFactory? _socketFactory;
        private StreamLogger? _streamLogger = null;
        private IPacketCapture? _packetCapture;
        private bool _hasDiagnoseStarted;
        private bool _hasDisconnectedByUser;
        private bool _hasAnyDataArrived;
        private bool _isConnecting;
        private bool _isDisconnecting;
        private bool _hasConnectRequested;
        private Exception? _lastException;
        private IpGroupManager? _ipGroupManager;
        private VpnHoodClient? Client => ClientConnect?.Client;

        public VpnHoodConnect? ClientConnect { get; private set; }
        public event EventHandler? ClientConnectCreated;
        public int Timeout { get; set; }
        public Diagnoser Diagnoser { get; set; } = new();
        public ClientProfile? ActiveClientProfile { get; private set; }
        public Guid LastActiveClientProfileId { get; private set; }
        public bool LogAnonymous { get; private set; }
        public static VpnHoodApp Instance => _instance ?? throw new InvalidOperationException($"{nameof(VpnHoodApp)} has not been initialized yet!");
        public static bool IsInit => _instance != null;
        public static VpnHoodApp Init(IAppProvider clientAppProvider, AppOptions? options = default)
        {
            return new VpnHoodApp(clientAppProvider, options);
        }
        /// <summary>
        /// Force to use this logger
        /// </summary>
        public string AppDataFolderPath { get; }
        public string LogFilePath => Path.Combine(AppDataFolderPath, FILENAME_Log);
        public AppSettings Settings { get; private set; }
        public UserSettings UserSettings => Settings.UserSettings;
        public AppFeatures Features { get; private set; }
        public ClientProfileStore ClientProfileStore { get; private set; }
        public IDevice Device => _clientAppProvider.Device;

        private VpnHoodApp(IAppProvider clientAppProvider, AppOptions? options = default)
        {
            if (IsInit) throw new InvalidOperationException($"{nameof(VpnHoodApp)} is already initialized!");
            if (options == null) options = new AppOptions();
            Directory.CreateDirectory(options.AppDataPath); //make sure directory exists

            _clientAppProvider = clientAppProvider ?? throw new ArgumentNullException(nameof(clientAppProvider));
            if (_clientAppProvider.Device == null) throw new ArgumentNullException(nameof(_clientAppProvider.Device));
            Device.OnStartAsService += Device_OnStartAsService;

            _logToConsole = options.LogToConsole;
            AppDataFolderPath = options.AppDataPath ?? throw new ArgumentNullException(nameof(options.AppDataPath));
            Settings = AppSettings.Load(Path.Combine(AppDataFolderPath, FILENAME_Settings));
            Settings.OnSaved += Settings_OnSaved;
            ClientProfileStore = new ClientProfileStore(Path.Combine(AppDataFolderPath, FOLDERNAME_ProfileStore));
            Features = new AppFeatures();
            Timeout = options.Timeout;
            _socketFactory = options.SocketFactory;

            // create default logger
            LogAnonymous = options.LogAnonymous;
            VhLogger.IsAnonymousMode = options.LogAnonymous;
            VhLogger.Instance = CreateLogger(false);

            // add default test public server if not added yet
            RemoveClientProfileByToken(Guid.Parse("2C02AC41-040F-4576-B8CC-DCFE5B9170B7")); //old one; deprecated in version v1.2.247 and upper
            if (Settings.TestServerTokenAutoAdded != Settings.TestServerToken.ToAccessKey())
            {
                Settings.TestServerTokenAutoAdded = Settings.TestServerAccessKey;
                ClientProfileStore.AddAccessKey(Settings.TestServerAccessKey);
            }

            Features.TestServerTokenId = Token.FromAccessKey(Settings.TestServerAccessKey).TokenId;
            Features.IsExcludeAppsSupported = Device.IsExcludeAppsSupported;
            Features.IsIncludeAppsSupported = Device.IsIncludeAppsSupported;

            _instance = this;
        }

        private bool RemoveClientProfileByToken(Guid guid)
        {
            var clientProfile = ClientProfileStore.ClientProfiles.FirstOrDefault(x => x.TokenId == guid);
            if (clientProfile == null)
                return false;
            ClientProfileStore.RemoveClientProfile(clientProfile.ClientProfileId);
            return true;
        }

        private void Device_OnStartAsService(object sender, EventArgs e)
        {
            var clientPrpfile = ClientProfileStore.ClientProfiles.FirstOrDefault(x => x.ClientProfileId == UserSettings.DefaultClientProfileId);
            if (clientPrpfile == null) ClientProfileStore.ClientProfiles.FirstOrDefault();
            if (clientPrpfile == null) throw new Exception("There is no default configuation!");

            var _ = Connect(clientPrpfile.ClientProfileId);
        }

        private string? LastError => _lastException?.Message ?? Client?.SessionStatus?.ErrorMessage;

        public AppState State => new()
        {
            ConnectionState = ConnectionState,
            IsIdle = IsIdle,
            ActiveClientProfileId = ActiveClientProfile?.ClientProfileId,
            DefaultClientProfileId = DefaultClientProfileId,
            LastActiveClientProfileId = LastActiveClientProfileId,
            LogExists = IsIdle && File.Exists(LogFilePath),
            LastError = LastError,
            HasDiagnoseStarted = _hasDiagnoseStarted,
            HasDisconnectedByUser = _hasDisconnectedByUser,
            HasProblemDetected = _hasConnectRequested && IsIdle && (_hasDiagnoseStarted || LastError != null),
            SessionStatus = Client?.SessionStatus,
            ReceiveSpeed = Client?.ReceiveSpeed ?? 0,
            RecievedByteCount = Client?.ReceivedByteCount ?? 0,
            SendSpeed = Client?.SendSpeed ?? 0,
            SentByteCount = Client?.SentByteCount ?? 0,
        };

        private Guid? DefaultClientProfileId
        {
            get
            {
                return ClientProfileStore.ClientProfileItems.Any(x => x.ClientProfile.ClientProfileId == UserSettings.DefaultClientProfileId)
                    ? UserSettings.DefaultClientProfileId
                    : ClientProfileStore.ClientProfileItems.FirstOrDefault()?.ClientProfile.ClientProfileId;
            }
            set
            {
                if (UserSettings.DefaultClientProfileId == value)
                    return;

                UserSettings.DefaultClientProfileId = value;
                Settings.Save();
            }
        }

        private AppConnectionState ConnectionState
        {
            get
            {
                if (Diagnoser.IsWorking) return AppConnectionState.Diagnosing;
                if (_isDisconnecting || Client?.State == ClientState.Disconnecting) return AppConnectionState.Disconnecting;
                if (_isConnecting || Client?.State == ClientState.Connecting) return AppConnectionState.Connecting;
                if (Client?.State == ClientState.Connected) return AppConnectionState.Connected;
                return AppConnectionState.None;
            }
        }
        public string GetLogForReport()
        {
            var log = File.ReadAllText(LogFilePath);

            // remove IPs
            if (LogAnonymous)
            {
                var pattern = @"(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})";
                log = Regex.Replace(log, pattern, "*.*.$3.$4");
            }

            return log;
        }

        public bool IsIdle => ConnectionState == AppConnectionState.None;

        private ILogger CreateLogger(bool addFileLogger)
        {

            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                // console
                if (_logToConsole)
                    builder.AddSimpleConsole((config) => { config.IncludeScopes = true; });

                // file logger, close old stream
                _streamLogger?.Dispose();
                _streamLogger = null;
                if (addFileLogger)
                {
                    var fileStream = new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                    _streamLogger = new StreamLogger(fileStream, true);
                    builder.AddProvider(_streamLogger);
                }

                builder.SetMinimumLevel(Settings.UserSettings.LogVerbose ? LogLevel.Trace : LogLevel.Information);
            });

            var logger = loggerFactory.CreateLogger("");
            return new SyncLogger(logger);
        }

        public void ClearLastError()
        {
            if (!IsIdle)
                return; //can just set in Idle State

            _lastException = null;
            _hasDiagnoseStarted = false;
            _hasDisconnectedByUser = false;
            _hasConnectRequested = false;
            _hasAnyDataArrived = false;
        }

        public async Task Connect(Guid clientProfileId, bool diagnose = false, string? userAgent = default)
        {
            // disconnect if user request diagnosing
            if ((ActiveClientProfile != null && ActiveClientProfile.ClientProfileId != clientProfileId) ||
                (!IsIdle && diagnose && !_hasDiagnoseStarted))
                Disconnect(true);

            // check already in progress
            if (ActiveClientProfile != null || !IsIdle)
            {
                var ex = new InvalidOperationException("Connection is already in progress!");
                VhLogger.Instance?.LogError(ex.Message);
                _lastException = ex;
                throw ex;
            }

            try
            {
                // prepare logger
                ClearLastError();
                _isConnecting = true;
                _hasConnectRequested = true;
                _hasDiagnoseStarted = diagnose;
                VhLogger.IsDiagnoseMode = diagnose;

                if (File.Exists(LogFilePath)) File.Delete(LogFilePath);
                var logger = CreateLogger(diagnose || Settings.UserSettings.LogToFile);
                VhLogger.Instance = new FilterLogger(logger, (eventId) =>
                {
                    if (eventId == GeneralEventId.Hello) return true;
                    if (eventId == GeneralEventId.Tcp) return diagnose;
                    if (eventId == GeneralEventId.Ping) return diagnose;
                    if (eventId == GeneralEventId.Nat) return diagnose;
                    if (eventId == GeneralEventId.Dns) return diagnose;
                    if (eventId == GeneralEventId.Udp) return diagnose;
                    if (eventId == GeneralEventId.StreamChannel) return diagnose;
                    if (eventId == GeneralEventId.DatagramChannel) return true;
                    return true;
                });

                // Set ActiveProfile
                ActiveClientProfile = ClientProfileStore.ClientProfiles.First(x => x.ClientProfileId == clientProfileId);
                DefaultClientProfileId = ActiveClientProfile.ClientProfileId;
                LastActiveClientProfileId = ActiveClientProfile.ClientProfileId;

                // create packet capture
                var packetCapture = await Device.CreatePacketCapture();
                if (packetCapture.IsMtuSupported)
                    packetCapture.Mtu = TunnelUtil.MtuWithoutFragmentation;

                // App filters
                if (packetCapture.CanExcludeApps && UserSettings.AppFiltersMode == FilterMode.Exclude) packetCapture.ExcludeApps = UserSettings.AppFilters;
                if (packetCapture.CanIncludeApps && UserSettings.AppFiltersMode == FilterMode.Include) packetCapture.IncludeApps = UserSettings.AppFilters;

                // connect
                await ConnectInternal(packetCapture, ActiveClientProfile.TokenId, userAgent);

            }
            catch (Exception ex)
            {
                //user may disconnect before connection closed
                if (!_hasDisconnectedByUser)
                {
                    VhLogger.Instance?.LogError(ex.Message);
                    _lastException = ex;
                    Disconnect();
                }
                throw;
            }
            finally
            {
                _isConnecting = false;
            }
        }

        private void Settings_OnSaved(object sender, EventArgs e)
        {
            if (ClientConnect?.Client != null)
                ClientConnect.Client.UseUdpChannel = UserSettings.UseUdpChannel;
        }

        private async Task ConnectInternal(IPacketCapture packetCapture, Guid tokenId, string? userAgent)
        {
            _packetCapture = packetCapture;
            packetCapture.OnStopped += PacketCapture_OnStopped;

            // log general info
            VhLogger.Instance.LogInformation($"AppVersion: {typeof(VpnHoodApp).Assembly.GetName().Version.ToString().Replace('.', ',')}, Time: {DateTime.Now}");
            VhLogger.Instance.LogInformation($"OS: {Device.OperatingSystemInfo}");
            VhLogger.Instance.LogInformation($"UserAgent: {userAgent}");

            // get token
            var token = ClientProfileStore.GetToken(tokenId, true, true);
            VhLogger.Instance.LogInformation($"TokenId: {VhLogger.FormatId(token.TokenId)}, SupportId: {VhLogger.FormatId(token.SupportId)}");

            // Create Client
            ClientConnect = new VpnHoodConnect(
                packetCapture: packetCapture,
                clientId: Settings.ClientId,
                token: token,
                new ClientOptions
                {
                    Timeout = Timeout,
                    ExcludeLocalNetwork = UserSettings.ExcludeLocalNetwork,
                    IncludeIpRanges = await GetIncludeIpRanges(UserSettings.IpGroupFiltersMode, UserSettings.IpGroupFilters),
                    PacketCaptureIncludeIpRanges = GetIncludeIpRanges(UserSettings.PacketCaptureIpRangesFilterMode, UserSettings.PacketCaptureIpRanges),
                    SocketFactory = _socketFactory,
                    UserAgent = userAgent
                },
                new ConnectOptions
                {
                    MaxReconnectCount = UserSettings.MaxReconnectCount,
                    UdpChannelMode = UserSettings.UseUdpChannel ? UdpChannelMode.On : UdpChannelMode.Off
                });
            ClientConnectCreated?.Invoke(this, EventArgs.Empty);

            if (_hasDiagnoseStarted)
                await Diagnoser.Diagnose(ClientConnect);
            else
                await Diagnoser.Connect(ClientConnect);
        }

        private IpRange[]? GetIncludeIpRanges(FilterMode filterMode, IpRange[]? ipRanges)
        {
            if (filterMode == FilterMode.All || Util.IsNullOrEmpty(ipRanges))
                return null;
            else if (filterMode == FilterMode.Include)
                return ipRanges;
            else
                return IpRange.Invert(ipRanges);
        }

        private async Task<IpRange[]?> GetIncludeIpRanges(FilterMode filterMode, string[]? ipGroupIds)
        {
            if (filterMode == FilterMode.All || Util.IsNullOrEmpty(ipGroupIds))
                return null;
            else if (filterMode == FilterMode.Include)
                return await GetIpRanges(ipGroupIds);
            else
                return IpRange.Invert(await GetIpRanges(ipGroupIds));
        }

        private async Task<IpRange[]> GetIpRanges(string[] ipGroupIds)
        {
            List<IpRange> ipRanges = new();
            foreach (var ipGroupId in ipGroupIds)
            {
                try
                {
                    if (ipGroupId.Equals("custom", StringComparison.OrdinalIgnoreCase))
                        ipRanges.AddRange(UserSettings.CustomIpRanges);
                    else
                        ipRanges.AddRange((await GetIpGroupManager()).GetIpRanges(ipGroupId));
                }
                catch (Exception ex)
                {
                    VhLogger.Instance.LogError(ex, $"Could not add {nameof(IpRange)} of Group {ipGroupId}");
                }
            }
            return IpRange.Sort(ipRanges);
        }

        private void PacketCapture_OnStopped(object sender, EventArgs e)
        {
            Disconnect();
        }

        public void Disconnect(bool byUser = false)
        {
            if (_isDisconnecting)
                return;

            try
            {
                _isDisconnecting = true;

                if (byUser)
                    _hasDisconnectedByUser = true;

                // check for any success
                if (Client != null)
                {
                    _hasAnyDataArrived = Client.ReceivedByteCount > 1000;
                    if (LastError == null && !_hasAnyDataArrived && UserSettings.IpGroupFiltersMode == FilterMode.All)
                        _lastException = new Exception("No data has been arrived!");
                }

                // check diagnose
                if (_hasDiagnoseStarted && LastError == null)
                    _lastException = new Exception("Diagnose has been finished and no issue has been detected.");

                // close client
                try
                {
                    ClientConnect?.Dispose();
                }
                catch (Exception ex)
                {
                    VhLogger.Instance.LogError($"Could not dispose client properly! Error: {ex}");
                }

                VhLogger.Instance = CreateLogger(false);
            }
            finally
            {
                ActiveClientProfile = null;
                _packetCapture = null;
                ClientConnect = null;
                _isConnecting = false;
                _isDisconnecting = false;
            }
        }

        public async Task<IpGroup[]> GetIpGroups()
        {
            var ipGroupManager = await GetIpGroupManager();
            //var customIpGroup = new IpGroup { IpGroupId = "custom", IpGroupName = "Custom" };
            //return ipGroupManager.IpGroups.Concat(new[] { customIpGroup }).ToArray();
            return ipGroupManager.IpGroups;
        }
        private async Task<IpGroupManager> GetIpGroupManager()
        {
            if (_ipGroupManager != null)
                return _ipGroupManager;

            var ipGroupsPath = Path.Combine(AppDataFolderPath, "Temp", "ipgroups");

            // AddFromIp2Location if hash has been changed
            using var memZipStream = new MemoryStream(Resource.IP2LOCATION_LITE_DB1);
            memZipStream.Seek(0, SeekOrigin.Begin);
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(memZipStream);
            var hashString = BitConverter.ToString(hash).Replace("-", "");
            var path = Path.Combine(ipGroupsPath, hashString);

            // create
            _ipGroupManager = new IpGroupManager(Path.Combine(path, FILENAME_IpGroups));
            if (!Directory.Exists(path))
            {
                try { Directory.Delete(ipGroupsPath, true); } catch { };
                memZipStream.Seek(0, SeekOrigin.Begin);
                using var zipArchive = new ZipArchive(memZipStream);
                using var stream = zipArchive.GetEntry("IP2LOCATION-LITE-DB1.CSV").Open();
                await _ipGroupManager.AddFromIp2Location(stream);
            }

            return _ipGroupManager;
        }

        public void Dispose()
        {
            if (_instance != null)
            {
                Settings?.Save();
                Disconnect();
                _instance = null;
            }
        }
    }
}
