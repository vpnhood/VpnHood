using VpnHood.Logger;
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
        private IDevice _device;
        public AppClientProfile ActiveClientProfile { get; private set; }

        public static VpnHoodApp Current => _current ?? throw new InvalidOperationException($"{nameof(VpnHoodApp)} has not been initialized yet!");
        public static bool IsInit => _current != null;
        public static VpnHoodApp Init(IAppProvider clientAppProvider, AppOptions options = null)
        {
            return new VpnHoodApp(clientAppProvider, options);
        }

        /// <summary>
        /// Force to use this logger
        /// </summary>
        public VpnHoodClient client { get; private set; }
        public string AppDataPath { get; }

        public event EventHandler OnStateChanged;
        public AppSettings Settings { get; private set; }
        public AppFeatures Features { get; private set; }
        public AppClientProfileStore ClientProfileStore { get; private set; }
        public VpnHoodApp(IAppProvider clientAppProvider, AppOptions options = null)
        {
            if (IsInit) throw new InvalidOperationException($"{nameof(VpnHoodApp)} is already initialized!");
            if (options == null) options = new AppOptions();
            Directory.CreateDirectory(options.AppDataPath); //make sure directory exists

            clientAppProvider.DeviceReadly += ClientAppProvider_DeviceReadly;
            _clientAppProvider = clientAppProvider ?? throw new ArgumentNullException(nameof(clientAppProvider));
            _logToConsole = options.LogToConsole;
            AppDataPath = options.AppDataPath ?? throw new ArgumentNullException(nameof(options.AppDataPath));
            Settings = AppSettings.Load(Path.Combine(AppDataPath, FILENAME_Settings));
            ClientProfileStore = new AppClientProfileStore(Path.Combine(AppDataPath, FOLDERNAME_ProfileStore));
            Features = new AppFeatures();
            Logger.Logger.Current = CreateLogger(true);
            _current = this;
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
                var state = client?.State ?? ClientState.None;
                if ((state == ClientState.None || state == ClientState.Disposed) && _device != null)
                    state = ClientState.Disconnecting;
                return state;
            }
        }
        public bool IsIdle => client?.State == null || client?.State == ClientState.None || client?.State == ClientState.Disposed;

        private ILogger CreateLogger(bool disableFileLogger = false)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                // console
                if (_logToConsole)
                    builder.AddConsole((config) => { config.IncludeScopes = true; });

                // file
                if (Settings.LogToFile && !disableFileLogger)
                {
                    _logStream?.Dispose();
                    _logStream = new FileStream(Path.Combine(AppDataPath, FILENAME_Log), FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                    builder.AddProvider(new StreamLogger(_logStream, true, true));
                }

                builder.SetMinimumLevel(Settings.LogVerbose ? LogLevel.Trace : LogLevel.Information);
            });

            var logger = loggerFactory.CreateLogger("");
            return new SyncLogger(logger);
        }

        private void ClientAppProvider_DeviceReadly(object sender, AppDeviceReadyEventArgs e)
        {
            var _ = Connect(e.Device);
        }

        public Exception LastException { get; private set; }

        public void Connect(Guid clientProfileId)
        {
            try
            {
                if (ActiveClientProfile != null || !IsIdle)
                    throw new InvalidOperationException("Connection is already in progress!");

                // prepare logger
                Logger.Logger.Current = CreateLogger();

                LastException = null;
                ActiveClientProfile = ClientProfileStore.ClientProfiles.First(x => x.ClientProfileId == clientProfileId);
                _clientAppProvider.PrepareDevice();
            }
            catch (Exception ex)
            {
                LastException = ex;
                Disconnect();
                throw;
            }
        }

        private async Task Connect(IDeviceInbound device)
        {

            try
            {
                _device = device;
                device.OnStopped += Device_OnStopped;
                Logger.Logger.Current = new FilterLogger(CreateLogger(), (eventId) =>
                {
                    if (eventId == CommonEventId.Nat) return false;
                    if (eventId == ClientEventId.DnsReply || eventId == ClientEventId.DnsRequest) return false;
                    return true;
                });

                var token = ClientProfileStore.GetToken(ActiveClientProfile.TokenId, true);
                Logger.Logger.Current.LogInformation($"ClientProfileInfo: TokenId: {token.TokenId}, SupportId: {token.SupportId}, ServerEndPoint: {token.ServerEndPoint}");

                // Create Client
                client = new VpnHoodClient(
                    device: device,
                    clientId: Settings.ClientId,
                    token: token,
                    new ClientOptions()
                    {
                        TcpIpChannelCount = 4,
                        IpResolveMode = IpResolveMode.Token,
                    });

                client.OnStateChanged += Client_OnStateChanged;
                await client.Connect();

            }
            catch (Exception ex)
            {
                Logger.Logger.Current?.LogError(ex.Message);
                LastException = ex;
                Disconnect();
            }
        }

        private void Client_OnStateChanged(object sender, EventArgs e)
        {
            OnStateChanged?.Invoke(this, e);
            switch (State.ConnectionState)
            {
                case ClientState.None:
                case ClientState.Disposed:
                    break;
            }
        }

        private void Device_OnStopped(object sender, EventArgs e)
        {
            Disconnect();

            var device = (IDevice)sender;
            device.OnStopped -= Device_OnStopped;
            if (device == _device)
                _device = null;
        }

        public void Disconnect()
        {
            ActiveClientProfile = null;

            client?.Dispose();
            client = null;

            Logger.Logger.Current = CreateLogger(true);
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
