using McMaster.Extensions.CommandLineUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
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
        public static bool IsFileAccessServer => AppSettings.RestBaseUrl == null;

        public static string AppSettingsFilePath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        public static FileAccessServer FileAccessServer;
        public static RestAccessServer RestAccessServer;

        static void Main(string[] args)
        {
            // load AppSettings
            if (File.Exists(AppSettingsFilePath))
                AppSettings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppSettingsFilePath));

            // init AccessServer
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

        private static void InitAccessServer()
        {
            if (AppSettings.RestBaseUrl != null)
            {
                RestAccessServer = new RestAccessServer(AppSettings.RestBaseUrl, AppSettings.RestAuthHeader);
                var authHeader = string.IsNullOrEmpty(AppSettings.RestAuthHeader) ? "<Notset>" : "*****";
                Console.WriteLine($"Using ResetAccessServer!\nBaseUri:{RestAccessServer.BaseUri}\nAuthHeader: {authHeader}");
            }
            else
            {
                FileAccessServer = new FileAccessServer(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tokens"));
            }
        }
        private static IAccessServer AccessServer => (IAccessServer)FileAccessServer ?? RestAccessServer;


        private static void GenerateToken(CommandLineApplication cmdApp)
        {
            var localIpAddress = Util.GetLocalIpAddress();
            cmdApp.Description = "Generate a token";
            var nameOption = cmdApp.Option("-name", $"ServerEndPoint. Default: <NoName>", CommandOptionType.SingleValue);
            var endPointOption = cmdApp.Option("-ep", $"ServerEndPoint. Default: {localIpAddress}:443", CommandOptionType.SingleValue);
            var maxClientOption = cmdApp.Option("-maxClient", $"MaximumClient. Default: 2", CommandOptionType.SingleValue);

            cmdApp.OnExecute(() =>
            {
                var accessServer = FileAccessServer;

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
                        tokenId = FileAccessServer.TokenIdFromSupportId(supportId);
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
            var accessItem = FileAccessServer.AccessItem_Read(tokenId).Result;
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
                if (FileAccessServer!=null && FileAccessServer.GetAllTokenIds().Length == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("There is no token in the store! Use the following command to create:");
                    Console.WriteLine("server gen -?");
                    Console.ResetColor();
                    return 0;
                }

                // run server
                using var server = new VpnHoodServer(AccessServer, new ServerOptions()
                {
                    Certificate = new X509Certificate2(DefaultCertFile, "1"),
                    TcpHostEndPoint = new IPEndPoint(IPAddress.Any, portNumber)
                });

                server.Start().Wait();
                Thread.Sleep(-1);
                return 0;
            });
        }
    }
}
