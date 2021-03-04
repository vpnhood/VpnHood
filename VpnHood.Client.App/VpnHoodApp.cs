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

namespace VpnHood.Client.App
{
    public class VpnHoodApp : IDisposable
    {
        private const string FILENAME_Log = "log.txt";
        private const string FILENAME_Settings = "settings.json";
        private const string FOLDERNAME_ProfileStore = "profiles";
        private readonly IAppProvider _clientAppProvider;
        private static VpnHoodApp _current;
        private readonly bool _logToConsole;
        private StreamLogger _streamLogger;
        private IPacketCapture _packetCapture;
        private VpnHoodClient _client;
        private bool _hasDiagnoseStarted;
        private bool _hasDisconnectedByUser;
        private bool _hasAnyDataArrived;
        private bool _isConnecting;
        private bool _isDisconnecting;
        private bool _hasConnectRequested;

        public int Timeout { get; set; }
        public Diagnoser Diagnoser { get; set; } = new Diagnoser();
        public ClientProfile ActiveClientProfile { get; private set; }
        public Guid LastActiveClientProfileId { get; private set; }
        public bool LogAnonymous { get; private set; }
        public static VpnHoodApp Current => _current ?? throw new InvalidOperationException($"{nameof(VpnHoodApp)} has not been initialized yet!");
        public static bool IsInit => _current != null;
        public static VpnHoodApp Init(IAppProvider clientAppProvider, AppOptions options = null)
        {
            return new VpnHoodApp(clientAppProvider, options);
        }

        /// <summary>
        /// Force to use this logger
        /// </summary>
        public string AppDataFolderPath { get; }
        public string LogFilePath => Path.Combine(AppDataFolderPath, FILENAME_Log);
        public AppSettings Settings { get; private set; }
        public AppUserSettings UserSettings => Settings.UserSettings;
        public AppFeatures Features { get; private set; }
        public ClientProfileStore ClientProfileStore { get; private set; }
        public Exception LastException { get; private set; }

        private VpnHoodApp(IAppProvider clientAppProvider, AppOptions options = null)
        {
            if (IsInit) throw new InvalidOperationException($"{nameof(VpnHoodApp)} is already initialized!");
            if (options == null) options = new AppOptions();
            Directory.CreateDirectory(options.AppDataPath); //make sure directory exists

            _clientAppProvider = clientAppProvider ?? throw new ArgumentNullException(nameof(clientAppProvider));
            if (_clientAppProvider.Device == null) throw new ArgumentNullException(nameof(_clientAppProvider.Device));
            clientAppProvider.Device.OnStartAsService += Device_OnStartAsService;

            _logToConsole = options.LogToConsole;
            AppDataFolderPath = options.AppDataPath ?? throw new ArgumentNullException(nameof(options.AppDataPath));
            Settings = AppSettings.Load(Path.Combine(AppDataFolderPath, FILENAME_Settings));
            ClientProfileStore = new ClientProfileStore(Path.Combine(AppDataFolderPath, FOLDERNAME_ProfileStore));
            Features = new AppFeatures();
            Timeout = options.Timeout;

            // create default logger
            LogAnonymous = options.LogAnonymous;
            VhLogger.IsAnonymousMode = options.LogAnonymous;
            VhLogger.Current = CreateLogger(false);

            // add default test public server if not added yet
            if (Settings.TestServerTokenIdAutoAdded != Settings.TestServerTokenId)
                Settings.TestServerTokenIdAutoAdded = ClientProfileStore.AddAccessKey(Settings.TestServerAccessKey).TokenId;
            Features.TestServerTokenId = Settings.TestServerTokenId;

            _current = this;
        }

        private void Device_OnStartAsService(object sender, EventArgs e)
        {
            var clientPrpfile = ClientProfileStore.ClientProfiles.FirstOrDefault(x => x.ClientProfileId == UserSettings.DefaultClientProfileId);
            if (clientPrpfile == null) ClientProfileStore.ClientProfiles.FirstOrDefault();
            if (clientPrpfile == null) throw new Exception("There is no default configuation!");

            var _ = Connect(clientPrpfile.ClientProfileId);
        }

        public AppState State => new AppState()
        {
            ConnectionState = ConnectionState,
            IsIdle = IsIdle,
            ActiveClientProfileId = ActiveClientProfile?.ClientProfileId,
            DefaultClientProfileId = DefaultClientProfileId,
            LastActiveClientProfileId = LastActiveClientProfileId,
            LogExists = IsIdle && File.Exists(LogFilePath),
            LastError = _hasConnectRequested ? LastException?.Message : null,
            HasDiagnoseStarted = _hasConnectRequested && _hasDiagnoseStarted,
            HasDisconnectedByUser = _hasConnectRequested && _hasDisconnectedByUser,
            HasProblemDetected = _hasConnectRequested && IsIdle && (!_hasAnyDataArrived || _hasDiagnoseStarted || (LastException != null && !_hasDisconnectedByUser)),
            SessionStatus = _client?.SessionStatus,
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
                if (_isDisconnecting || _client?.State == ClientState.Disconnecting) return AppConnectionState.Disconnecting;
                if (_isConnecting || _client?.State == ClientState.Connecting) return AppConnectionState.Connecting;
                if (_client?.State == ClientState.Connected) return AppConnectionState.Connected;
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

            LastException = null;
            _hasAnyDataArrived = false;
            _hasDiagnoseStarted = false;
            _hasDisconnectedByUser = false;
            _hasConnectRequested = false;
        }

        public async Task Connect(Guid clientProfileId, bool diagnose = false, string userAgent = null)
        {
            try
            {
                // disconnect if user request diagnosing
                if (diagnose && !_hasDiagnoseStarted && !IsIdle)
                    Disconnect(true);

                // check already in progress
                if (ActiveClientProfile != null || !IsIdle)
                    throw new InvalidOperationException("Connection is already in progress!");

                // prepare logger
                ClearLastError();
                _isConnecting = true;
                _hasConnectRequested = true;
                _hasDiagnoseStarted = diagnose;
                VhLogger.IsDiagnoseMode = diagnose;

                if (File.Exists(LogFilePath)) File.Delete(LogFilePath);
                var logger = CreateLogger(diagnose || Settings.UserSettings.LogToFile);
                VhLogger.Current = new FilterLogger(logger, (eventId) =>
                {
                    if (eventId == GeneralEventId.Hello) return true;
                    if (eventId == GeneralEventId.Tcp) return diagnose;
                    if (eventId == GeneralEventId.TcpProxy) return diagnose;
                    if (eventId == GeneralEventId.TcpDatagram) return true;
                    if (eventId == GeneralEventId.Ping) return diagnose;
                    if (eventId == GeneralEventId.Nat) return diagnose;
                    if (eventId == GeneralEventId.Dns) return diagnose;
                    if (eventId == GeneralEventId.Udp) return true;
                    return true;
                });

                // Set ActiveProfile
                ActiveClientProfile = ClientProfileStore.ClientProfiles.First(x => x.ClientProfileId == clientProfileId);
                DefaultClientProfileId = ActiveClientProfile.ClientProfileId;
                LastActiveClientProfileId = ActiveClientProfile.ClientProfileId;

                // connect
                var packetCapture = await _clientAppProvider.Device.CreatePacketCapture();
                await ConnectInternal(packetCapture, userAgent);

            }
            catch (Exception ex)
            {
                //user may disconnect before connection closed
                if (!_hasDisconnectedByUser)
                {
                    VhLogger.Current?.LogError(ex.Message);
                    LastException = ex;
                    Disconnect();
                }
                throw;
            }
            finally
            {
                _isConnecting = false;
            }
        }

        private async Task ConnectInternal(IPacketCapture packetCapture, string userAgent)
        {
            _packetCapture = packetCapture;
            packetCapture.OnStopped += PacketCapture_OnStopped;

            // log general info
            VhLogger.Current.LogInformation($"AppVersion: {typeof(VpnHoodApp).Assembly.GetName().Version.ToString().Replace('.', ',')}, Time: {DateTime.Now}");
            VhLogger.Current.LogInformation($"OS: {_clientAppProvider.OperatingSystemInfo}");
            VhLogger.Current.LogInformation($"UserAgent: {userAgent}");

            // get token
            var token = ClientProfileStore.GetToken(ActiveClientProfile.TokenId, true, true);
            VhLogger.Current.LogInformation($"TokenId: {VhLogger.FormatId(token.TokenId)}, SupportId: {VhLogger.FormatId(token.SupportId)}, ServerEndPoint: {VhLogger.FormatDns(token.ServerEndPoint)}");

            // Create Client
            _client = new VpnHoodClient(
                packetCapture: packetCapture,
                clientId: Settings.ClientId,
                token: token,
                new ClientOptions()
                {
                    MaxReconnectCount = Settings.UserSettings.MaxReconnectCount,
                    Timeout = Timeout,
                    Version = Features.Version
                });

            if (_hasDiagnoseStarted)
                await Diagnoser.Diagnose(_client);
            else
                await Diagnoser.Connect(_client);
        }

        private void PacketCapture_OnStopped(object sender, EventArgs e)
        {
            _packetCapture.OnStopped -= PacketCapture_OnStopped; // make sure no recursive call
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
                if (_client != null)
                {
                    if (_client.ReceivedByteCount > 1000)
                        _hasAnyDataArrived = true;
                    else if (LastException == null)
                        LastException = new Exception("No data has been arrived!");
                }

                // check diagnose
                if (_hasDiagnoseStarted && LastException == null)
                    LastException = new Exception("Diagnose has been finished and no issue has been detected.");

                ActiveClientProfile = null;

                // close client
                _client?.Dispose();
                _client = null;

                // close packet capture
                if (_packetCapture != null)
                {
                    _packetCapture.OnStopped -= PacketCapture_OnStopped;
                    _packetCapture.Dispose();
                    _packetCapture = null;
                }

                VhLogger.Current = CreateLogger(false);
            }
            finally
            {
                _isConnecting = false;
                _isDisconnecting = false;
            }
        }

        public void Dispose()
        {
            Settings?.Save();
            Disconnect();
            if (_current == this)
                _current = null;
        }
    }
}
