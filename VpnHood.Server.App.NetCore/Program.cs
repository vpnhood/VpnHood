using McMaster.Extensions.CommandLineUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;

namespace VpnHood.Server.App
{

    class Program
    {
        const string DefaultCertFile = "certs/testlibrary.org.pfx";

        static void Main(string[] args)
        {
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
            cmdApp.Command("print", PrintToken);
            cmdApp.Command("gen", GenerateToken);

            try
            {
                cmdApp.Execute(args);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        static FileTokenStore GetTokenStore()
        {
            var tokenStore = new FileTokenStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tokens"));
            return tokenStore;
        }

        private static void GenerateToken(CommandLineApplication cmdApp)
        {
            var localIpAddress = Util.GetLocalIpAddress();
            cmdApp.Description = "Generate a token";
            var endPointOption = cmdApp.Option("-ep", $"ServerEndPoint. Default: {localIpAddress}:443", CommandOptionType.SingleValue);

            cmdApp.OnExecute(() =>
            {
                var tokenStore = GetTokenStore();

                // generate key
                var aes = Aes.Create();
                aes.KeySize = 128;
                aes.GenerateKey();

                // read certificate
                var certificate = new X509Certificate2(DefaultCertFile, "1");

                // create tokenInfo
                var tokenInfo = new TokenInfo()
                {
                    Token = new Token()
                    {
                        TokenId = Guid.NewGuid(),
                        ServerEndPoint = endPointOption.HasValue() ? IPEndPoint.Parse(endPointOption.Value()) : IPEndPoint.Parse($"{localIpAddress }:443"),
                        Secret = aes.Key,
                        DnsName = certificate.GetNameInfo(X509NameType.DnsName, false),
                        PublicKeyHash = Token.ComputePublicKeyHash(certificate.GetPublicKey())
                    }
                };

                if (tokenInfo.Token.ServerEndPoint.Port == 0) tokenInfo.Token.ServerEndPoint.Port = 443; //set defult port

                Console.WriteLine($"The following token has been generated: ");
                tokenStore.AddToken(tokenInfo);
                PrintToken(tokenInfo.Token.TokenId);
                Console.WriteLine($"Store Token Count: {tokenStore.GetAllTokenIds().Length}");
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
                        tokenId = GetTokenStore().TokenIdFromSupportId(supportId);
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
            var tokenInfo = GetTokenStore().GetTokenInfo(tokenId, true);
            if (tokenInfo == null) throw new KeyNotFoundException($"Token does not exist! tokenId: {tokenId}");
            Console.WriteLine($"");
            Console.WriteLine($"TokenId: {tokenId}");
            Console.WriteLine($"supportId: {tokenInfo.Token.SupportId}");
            Console.WriteLine($"{JsonSerializer.Serialize(tokenInfo)}");
            Console.WriteLine($"ServerEndPoint: {tokenInfo.Token.ServerEndPoint}");
            Console.WriteLine($"DnsName: {tokenInfo.Token.DnsName}");
            Console.WriteLine($"---");
            Console.WriteLine(tokenInfo.Token.ToAccessKey());
            Console.WriteLine($"---");
        }

        private static void RunServer(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Run the server (default command)";
            var portOption = cmdApp.Option("-p|--port", "listening port. default is 443 (https)", CommandOptionType.SingleValue);
            cmdApp.OnExecute(() =>
            {
                var portNumber = portOption.HasValue() ? int.Parse(portOption.Value()) : 443;
                var tokenStore = GetTokenStore();

                // check tokenStore
                if (tokenStore.GetAllTokenIds().Length == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("There is no token in the store! Use the following command to create:");
                    Console.WriteLine("server gen -?");
                    Console.ResetColor();
                    return 0;
                }


                using var server = new VpnHoodServer(tokenStore, new ServerOptions()
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
