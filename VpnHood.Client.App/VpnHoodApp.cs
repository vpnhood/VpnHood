using VpnHood.Loggers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PacketDotNet.Tcp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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
        private Stream _logStream;
        private IPacketCapture _packetCapture;
        public ClientProfile ActiveClientProfile { get; private set; }

        public static VpnHoodApp Current => _current ?? throw new InvalidOperationException($"{nameof(VpnHoodApp)} has not been initialized yet!");
        public static bool IsInit => _current != null;
        public static VpnHoodApp Init(IAppProvider clientAppProvider, AppOptions options = null)
        {
            return new VpnHoodApp(clientAppProvider, options);
        }

        /// <summary>
        /// Force to use this logger
        /// </summary>
        public VpnHoodClient Client { get; private set; }
        public string AppDataPath { get; }

        public event EventHandler OnStateChanged;
        public AppSettings Settings { get; private set; }
        public AppUserSettings UserSettings => Settings.UserSettings;
        public AppFeatures Features { get; private set; }
        public ClientProfileStore ClientProfileStore { get; private set; }
        private VpnHoodApp(IAppProvider clientAppProvider, AppOptions options = null)
        {
            if (IsInit) throw new InvalidOperationException($"{nameof(VpnHoodApp)} is already initialized!");
            if (options == null) options = new AppOptions();
            Directory.CreateDirectory(options.AppDataPath); //make sure directory exists

            _clientAppProvider = clientAppProvider ?? throw new ArgumentNullException(nameof(clientAppProvider));
            if (_clientAppProvider.Device == null) throw new ArgumentNullException(nameof(_clientAppProvider.Device));
            clientAppProvider.Device.OnStartAsService += Device_OnStartAsService;

            _logToConsole = options.LogToConsole;
            AppDataPath = options.AppDataPath ?? throw new ArgumentNullException(nameof(options.AppDataPath));
            Settings = AppSettings.Load(Path.Combine(AppDataPath, FILENAME_Settings));
            ClientProfileStore = new ClientProfileStore(Path.Combine(AppDataPath, FOLDERNAME_ProfileStore));
            Features = new AppFeatures();

            Logger.Current = CreateLogger(true);
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
            LastError = LastException?.Message
        };

        private ClientState ConnectionState
        {
            get
            {
                var state = Client?.State ?? ClientState.None;
                if ((state == ClientState.None || state == ClientState.IsDisposed) && _packetCapture != null)
                    state = ClientState.Disconnecting;
                return state;
            }
        }
        public bool IsIdle => Client?.State == null || Client?.State == ClientState.None || Client?.State == ClientState.IsDisposed;

        private ILogger CreateLogger(bool disableFileLogger = false)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                // console
                if (_logToConsole)
                    builder.AddConsole((config) => { config.IncludeScopes = true; });

                // file
                if (Settings.UserSettings.LogToFile && !disableFileLogger)
                {
                    _logStream?.Dispose();
                    _logStream = new FileStream(Path.Combine(AppDataPath, FILENAME_Log), FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                    builder.AddProvider(new StreamLogger(_logStream, true, true));
                }

                builder.SetMinimumLevel(Settings.UserSettings.LogVerbose ? LogLevel.Trace : LogLevel.Information);
            });

            var logger = loggerFactory.CreateLogger("");
            return new SyncLogger(logger);
        }

        public void ClearLastError()
        {
            LastException = null;
        }

        public Exception LastException { get; private set; }

        public async Task Connect(Guid clientProfileId)
        {
            try
            {
                if (ActiveClientProfile != null || !IsIdle)
                    throw new InvalidOperationException("Connection is already in progress!");

                // prepare logger
                LastException = null;
                Logger.Current = CreateLogger();

                ActiveClientProfile = ClientProfileStore.ClientProfiles.First(x => x.ClientProfileId == clientProfileId);
                var packetCapture = await _clientAppProvider.Device.CreatePacketCapture();
                await Connect(packetCapture);

                // set default ClientProfile
                if (UserSettings.DefaultClientProfileId != ActiveClientProfile.ClientProfileId)
                {
                    UserSettings.DefaultClientProfileId = ActiveClientProfile.ClientProfileId;
                    Settings.Save();
                }

            }
            catch (Exception ex)
            {
                Logger.Current?.LogError(ex.Message);
                LastException = ex;
                Disconnect();
                throw;
            }
        }

        private async Task Connect(IPacketCapture packetCapture)
        {
            _packetCapture = packetCapture;
            packetCapture.OnStopped += PacketCapture_OnStopped;
            Logger.Current = new FilterLogger(CreateLogger(), (eventId) =>
            {
                if (eventId == CommonEventId.Nat) return false;
                if (eventId == ClientEventId.DnsReply || eventId == ClientEventId.DnsRequest) return false;
                return true;
            });

            var token = ClientProfileStore.GetToken(ActiveClientProfile.TokenId, true);
            Logger.Current.LogInformation($"ClientProfileInfo: TokenId: {token.TokenId}, SupportId: {token.SupportId}, ServerEndPoint: {token.ServerEndPoint}");

            // Create Client
            Client = new VpnHoodClient(
                packetCapture: packetCapture,
                clientId: Settings.ClientId,
                token: token,
                new ClientOptions()
                {
                    TcpIpChannelCount = 4,
                    IpResolveMode = IpResolveMode.Token,
                });

            Client.OnStateChanged += Client_OnStateChanged;
            await Client.Connect();
        }

        private void Client_OnStateChanged(object sender, EventArgs e)
        {
            OnStateChanged?.Invoke(this, e);
            switch (State.ConnectionState)
            {
                case ClientState.None:
                case ClientState.IsDisposed:
                    break;
            }
        }

        private void PacketCapture_OnStopped(object sender, EventArgs e)
        {
            Disconnect();

            var packetCapture = (IPacketCapture)sender;
            packetCapture.OnStopped -= PacketCapture_OnStopped;
            if (packetCapture == _packetCapture)
                _packetCapture = null;
        }

        public void Disconnect()
        {
            ActiveClientProfile = null;

            Client?.Dispose();
            Client = null;

            Logger.Current = CreateLogger(true);
            _logStream?.Dispose();
            _logStream = null;
        }

        public void Dispose()
        {
            Disconnect();
            if (_current == this)
                _current = null;
        }
    }
}
