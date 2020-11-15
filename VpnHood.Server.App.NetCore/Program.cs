using McMaster.Extensions.CommandLineUtils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using VpnHood.Server.AccessServers;

namespace VpnHood.Server.App
{
    class Program
    {
        const string DefaultCertFile = "certs/testlibrary.org.pfx";
        public static AppSettings AppSettings { get; set; } = new AppSettings();
        public static AppData AppData { get; set; } = new AppData();
        public static bool IsFileAccessServer => AppSettings.RestBaseUrl == null;
        private static FileAccessServer _fileAccessServer;
        private static RestAccessServer _restAccessServer;
        private static readonly AppUpdater _appUpdater = new AppUpdater();
        private static VpnHoodServer _vpnHoodServer;
        private static GoogleAnalytics _googleAnalytics;

        static void Main(string[] args)
        {
            // Report current Version
            Console.WriteLine();
            Console.WriteLine($"AccessServer. Version: {typeof(Program).Assembly.GetName().Version}");

            // check new version
            if (_appUpdater.LaunchNewVersion())
                return;
            _appUpdater.NewVersionFound += AppUpdater_NewVersionFound;

            //Init AppData
            LoadAppData();

            // load AppSettings
            var _appSettingsFilePath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            if (File.Exists(_appSettingsFilePath))
                AppSettings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_appSettingsFilePath));

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
                Name = "server",
                FullName = "VpnHood server",
                MakeSuggestionsInErrorMessage = true,
            };

            cmdApp.HelpOption(true);
            cmdApp.VersionOption("-n|--version", typeof(Program).Assembly.GetName().Version.ToString());

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
                Console.WriteLine(ex.Message);
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

            _googleAnalytics = new GoogleAnalytics(trackId: "UA-183010362-1", anonyClientId: AppData.ServerId.ToString());
        }

        private static void AppUpdater_NewVersionFound(object sender, EventArgs e)
        {
            _vpnHoodServer?.Dispose();
        }

        private static void InitAccessServer()
        {
            if (AppSettings.RestBaseUrl != null)
            {
                _restAccessServer = new RestAccessServer(AppSettings.RestBaseUrl, AppSettings.RestAuthHeader);
                var authHeader = string.IsNullOrEmpty(AppSettings.RestAuthHeader) ? "<Notset>" : "*****";
                Console.WriteLine($"Using ResetAccessServer!\nBaseUri:{_restAccessServer.BaseUri}\nAuthHeader: {authHeader}");
            }
            else
            {
                _fileAccessServer = new FileAccessServer(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tokens"));
            }
        }
        private static IAccessServer AccessServer => (IAccessServer)_fileAccessServer ?? _restAccessServer;


        private static void GenerateToken(CommandLineApplication cmdApp)
        {
            var localIpAddress = Util.GetLocalIpAddress();
            cmdApp.Description = "Generate a token";
            var nameOption = cmdApp.Option("-name", $"ServerEndPoint. Default: <NoName>", CommandOptionType.SingleValue);
            var endPointOption = cmdApp.Option("-ep", $"ServerEndPoint. Default: {localIpAddress}:443", CommandOptionType.SingleValue);
            var maxClientOption = cmdApp.Option("-maxClient", $"MaximumClient. Default: 2", CommandOptionType.SingleValue);

            cmdApp.OnExecute(() =>
            {
                var accessServer = _fileAccessServer;

                // generate key
                var aes = Aes.Create();
                aes.KeySize = 128;
                aes.GenerateKey();

                // read certificate
                var certificate = new X509Certificate2(DefaultCertFile, "1");

                var serverEndPoint = endPointOption.HasValue() ? IPEndPoint.Parse(endPointOption.Value()) : IPEndPoint.Parse($"{localIpAddress}:{AppSettings.Port}");
                if (serverEndPoint.Port == 0) serverEndPoint.Port = AppSettings.Port; //set defult port

                // create AccessItem
                var accessItem = new FileAccessServer.AccessItem()
                {
                    MaxClientCount = maxClientOption.HasValue() ? int.Parse(maxClientOption.Value()) : 2,
                    Token = new Token()
                    {
                        Name = nameOption.HasValue() ? nameOption.Value() : null,
                        TokenId = Guid.NewGuid(),
                        ServerEndPoint = serverEndPoint.ToString(),
                        Secret = aes.Key,
                        DnsName = certificate.GetNameInfo(X509NameType.DnsName, false),
                        PublicKeyHash = Token.ComputePublicKeyHash(certificate.GetPublicKey())
                    }
                };


                Console.WriteLine($"The following token has been generated: ");
                accessServer.AddAccessItem(accessItem);
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
                    Console.WriteLine("There is no token in the store! Use the following command to create:");
                    Console.WriteLine("server gen -?");
                    Console.ResetColor();
                    return 0;
                }

                // run server
                _vpnHoodServer = new VpnHoodServer(AccessServer, new ServerOptions()
                {
                    Certificate = new X509Certificate2(DefaultCertFile, "1"),
                    TcpHostEndPoint = new IPEndPoint(IPAddress.Any, portNumber),
                    Tracker = _googleAnalytics
                });

                _vpnHoodServer.Start().Wait();
                while (_vpnHoodServer.State != ServerState.Disposed)
                    Thread.Sleep(1000);

                // launch new version
                if (_appUpdater.NewAppPath != null)
                    _appUpdater.LaunchNewVersion();
                return 0;
            });
        }
    }

}
