using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using VpnHood.Common;
using VpnHood.Common.Trackers;
using VpnHood.Logging;
using VpnHood.Server.AccessServers;

namespace VpnHood.Server.App
{
    class Program
    {
        public static AppSettings AppSettings { get; set; } = new AppSettings();
        public static AppData AppData { get; set; } = new AppData();
        public static bool IsFileAccessServer => AppSettings.RestBaseUrl == null;
        private static FileAccessServer _fileAccessServer;
        private static RestAccessServer _restAccessServer;
        private static AppUpdater _appUpdater;
        private static VpnHoodServer _vpnHoodServer;
        private static GoogleAnalyticsTracker _googleAnalytics;
        private static AssemblyName AssemblyName => typeof(Program).Assembly.GetName();
        private static IAccessServer AccessServer => (IAccessServer)_fileAccessServer ?? _restAccessServer;

        public static string AppFolderPath { get; private set; }

        static void Main(string[] args)
        {
            // Report current Version
            // Replace dot in version to prevent anonymouizer treat it as ip.
            VhLogger.Current.LogInformation($"AccessServer. Version: {AssemblyName.Version.ToString().Replace('.', ',')}, Time: {DateTime.Now}");
            AppFolderPath = Path.GetDirectoryName(typeof(Program).Assembly.Location);
            if (File.Exists(Path.Combine(Path.GetDirectoryName(AppFolderPath), "publish.json")))
                AppFolderPath = Path.GetDirectoryName(AppFolderPath);

            // check update
            _appUpdater = new AppUpdater(AppFolderPath);
            _appUpdater.Updated += (sender, e) => _vpnHoodServer?.Dispose();
            _appUpdater.Start();
            if (_appUpdater.IsUpdated)
            {
                _appUpdater.LaunchUpdated();
                return;
            }

            //Init AppData
            LoadAppData();

            // load AppSettings
            var appSettingsFilePath = Path.Combine(AppFolderPath, "appsettings.json");
            if (File.Exists(appSettingsFilePath))
                AppSettings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(appSettingsFilePath));

            // track run
            _googleAnalytics.IsEnabled = AppSettings.IsAnonymousTrackerEnabled;
            _googleAnalytics.TrackEvent("Usage", "ServerRun");

            // init AccessServer
            if (AppSettings.IsAnonymousTrackerEnabled)
                InitAccessServer();

            // replace "/?"
            for (var i = 0; i < args.Length; i++)
                if (args[i] == "/?") args[i] = "-?";

            // set default
            if (args.Length == 0) args = new string[] { "run" };

            var cmdApp = new CommandLineApplication()
            {
                AllowArgumentSeparator = true,
                Name = AssemblyName.Name,
                FullName = "VpnHood server",
                MakeSuggestionsInErrorMessage = true,
            };

            cmdApp.HelpOption(true);
            cmdApp.VersionOption("-n|--version", AssemblyName.Version.ToString());

            cmdApp.Command("run", RunServer);

            // show file access server options
            if (IsFileAccessServer)
            {
                cmdApp.Command("print", PrintToken);
                cmdApp.Command("gen", GenerateToken);
            }

            try
            {
                cmdApp.Execute(args);
            }
            catch (ArgumentException ex)
            {
                VhLogger.Current.LogError(ex.Message);
            }
        }

        private static void LoadAppData()
        {
            // try to load
            var appDataFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnHood.Server", "AppData.json");
            try
            {
                var json = File.ReadAllText(appDataFilePath);
                AppData = JsonSerializer.Deserialize<AppData>(json);
            }
            catch { }

            if (AppData == null)
                AppData = new AppData();

            // set serverId if not exists
            if (AppData.ServerId == Guid.Empty)
            {
                AppData.ServerId = Guid.NewGuid();
                var json = JsonSerializer.Serialize(AppData);
                Directory.CreateDirectory(Path.GetDirectoryName(appDataFilePath));
                File.WriteAllText(appDataFilePath, json);
            }

            _googleAnalytics = new GoogleAnalyticsTracker(trackId: "UA-183010362-1", anonyClientId: AppData.ServerId.ToString());
        }

        private static void InitAccessServer()
        {
            if (AppSettings.RestBaseUrl != null)
            {
                _restAccessServer = new RestAccessServer(AppSettings.RestBaseUrl, AppSettings.RestAuthHeader);
                var authHeader = string.IsNullOrEmpty(AppSettings.RestAuthHeader) ? "<Notset>" : "*****";
                VhLogger.Current.LogInformation($"Using ResetAccessServer!, BaseUri: {_restAccessServer.BaseUri}, AuthHeader: {authHeader}");
            }
            else
            {
                var accessServerFolder = Path.Combine(AppFolderPath, "access");
                _fileAccessServer = new FileAccessServer(accessServerFolder, AppSettings.SslCertificatesPassword);
                VhLogger.Current.LogInformation($"Using FileAccessServer!, AccessFolder: {accessServerFolder}");
            }
        }

        private static void GenerateToken(CommandLineApplication cmdApp)
        {
            var localIpAddress = Util.GetLocalIpAddress();
            cmdApp.Description = "Generate a token";
            var nameOption = cmdApp.Option("-name", $"TokenName. Default: <NoName>", CommandOptionType.SingleValue);
            var endPointOption = cmdApp.Option("-ep", $"ServerEndPoint. Default: {localIpAddress}:443", CommandOptionType.SingleValue);
            var maxClientOption = cmdApp.Option("-maxClient", $"MaximumClient. Default: 2", CommandOptionType.SingleValue);

            cmdApp.OnExecute(() =>
            {
                var accessServer = _fileAccessServer;
                var serverEndPoint = endPointOption.HasValue() ? IPEndPoint.Parse(endPointOption.Value()) : IPEndPoint.Parse($"{localIpAddress}:{AppSettings.Port}");
                if (serverEndPoint.Port == 0) serverEndPoint.Port = AppSettings.Port; //set defult port

                var accessItem = accessServer.CreateAccessItem(
                    maxClientCount: maxClientOption.HasValue() ? int.Parse(maxClientOption.Value()) : 2,
                    serverEndPoint: serverEndPoint);

                Console.WriteLine($"The following token has been generated: ");
                PrintToken(accessItem.Token.TokenId);
                Console.WriteLine($"Store Token Count: {accessServer.GetAllTokenIds().Length}");
                return 0;
            });
        }

        private static void PrintToken(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Print a token";

            var tokenIdArg = cmdApp.Argument("tokenId", "tokenId (Guid) or tokenSupportId (id) to print");
            cmdApp.OnExecute(() =>
            {
                if (!Guid.TryParse(tokenIdArg.Value, out Guid tokenId))
                {
                    var supportId = int.Parse(tokenIdArg.Value);
                    try
                    {
                        tokenId = _fileAccessServer.TokenIdFromSupportId(supportId);
                    }
                    catch (KeyNotFoundException)
                    {
                        throw new KeyNotFoundException($"supportId does not exist! supportId: {supportId}");
                    }
                }

                PrintToken(tokenId);
                return 0;
            });
        }

        private static void PrintToken(Guid tokenId)
        {
            var accessItem = _fileAccessServer.AccessItem_Read(tokenId).Result;
            if (accessItem == null) throw new KeyNotFoundException($"Token does not exist! tokenId: {tokenId}");

            var access = AccessServer.GetAccess(new ClientIdentity() { TokenId = tokenId }).Result;
            if (access == null) throw new KeyNotFoundException($"Token does not exist! tokenId: {tokenId}");

            Console.WriteLine($"Token:");
            Console.WriteLine(JsonSerializer.Serialize(accessItem.Token, new JsonSerializerOptions() { WriteIndented = true }));
            Console.WriteLine($"---");
            Console.WriteLine(accessItem.Token.ToAccessKey());
            Console.WriteLine($"---");
            Console.WriteLine();
            Console.WriteLine($"Access:");
            Console.WriteLine(JsonSerializer.Serialize(access, new JsonSerializerOptions() { WriteIndented = true }));
        }

        private static void RunServer(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Run the server (default command)";
            var portOption = cmdApp.Option("-p|--port", "listening port. default is 443 (https)", CommandOptionType.SingleValue);
            cmdApp.OnExecute(() =>
            {
                var portNumber = portOption.HasValue() ? int.Parse(portOption.Value()) : 443;

                // check FileAccessServer
                if (_fileAccessServer != null && _fileAccessServer.GetAllTokenIds().Length == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    VhLogger.Current.LogInformation("There is no token in the store! Use the following command to create:");
                    VhLogger.Current.LogInformation("server gen -?");
                    Console.ResetColor();
                    return 0;
                }

                // run server
                _vpnHoodServer = new VpnHoodServer(AccessServer, new ServerOptions()
                {
                    TcpHostEndPoint = new IPEndPoint(IPAddress.Any, portNumber),
                    Tracker = _googleAnalytics
                });

                _vpnHoodServer.Start().Wait();
                while (_vpnHoodServer.State != ServerState.Disposed)
                    Thread.Sleep(1000);

                // launch new version
                if (_appUpdater.IsUpdated)
                    _appUpdater.LaunchUpdated();
                return 0;
            });
        }
    }

}
