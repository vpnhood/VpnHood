using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using VpnHood.Common;
using VpnHood.Common.Net;
using VpnHood.Server.AccessServers;

namespace VpnHood.Server.App
{
    public class FileAccessServerCommand
    {
        private readonly FileAccessServer _fileAccessServer;

        public FileAccessServerCommand(FileAccessServer fileAccessServer)
        {
            _fileAccessServer = fileAccessServer;
        }

        private static AppSettings AppSettings => ServerApp.Instance.AppSettings;

        public void AddCommands(CommandLineApplication cmdApp)
        {
            cmdApp.Command("print", PrintToken);
            cmdApp.Command("gen", GenerateToken);
        }

        private void PrintToken(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Print a token";

            var tokenIdArg = cmdApp.Argument("tokenId", "tokenId to print");
            cmdApp.OnExecuteAsync(async _ =>
            {
                await PrintToken(Guid.Parse(tokenIdArg.Value!));
                return 0;
            });
        }

        private async Task PrintToken(Guid tokenId)
        {
            var accessItem = await _fileAccessServer.AccessItem_Read(tokenId);
            if (accessItem == null) throw new KeyNotFoundException($"Token does not exist! tokenId: {tokenId}");

            var hostName = accessItem.Token.HostName + (accessItem.Token.IsValidHostName ? "" : " (Fake)");
            var endPoints = accessItem.Token.HostEndPoints?.Select(x => x.ToString()) ?? Array.Empty<string>();

            Console.WriteLine();
            Console.WriteLine("Access Details:");
            Console.WriteLine(JsonSerializer.Serialize(accessItem.AccessUsage,
                new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine();
            Console.WriteLine($"{nameof(Token.SupportId)}: {accessItem.Token.SupportId}");
            Console.WriteLine($"{nameof(Token.HostEndPoints)}: {string.Join(",", endPoints)}");
            Console.WriteLine($"{nameof(Token.HostName)}: {hostName}");
            Console.WriteLine($"{nameof(Token.HostPort)}: {accessItem.Token.HostPort}");
            Console.WriteLine($"TokenUpdateUrl: {accessItem.Token.Url}");
            Console.WriteLine("---");

            Console.WriteLine();
            Console.WriteLine("AccessKey:");
            Console.WriteLine(accessItem.Token.ToAccessKey());
            Console.WriteLine("---");
            Console.WriteLine();
        }

        private void GenerateToken(CommandLineApplication cmdApp)
        {
            // prepare default public ip
            var publicIps = IPAddressUtil.GetPublicIpAddresses().Result;
            var defaultPublicEps = new List<IPEndPoint>();
            var allListnerPorts = AppSettings.EndPoints.Select(x => x.Port).Distinct();
            foreach (var port in allListnerPorts)
                defaultPublicEps.AddRange(publicIps.Select(x => new IPEndPoint(x, port)));

            var publicEndPointDesc = $"PublicEndPoint. Default: {string.Join(",", defaultPublicEps)}";

            cmdApp.Description = "Generate a token";
            var nameOption = cmdApp.Option("-name", "TokenName. Default: <NoName>", CommandOptionType.SingleValue);
            var publicEndPointOption = cmdApp.Option("-ep", publicEndPointDesc, CommandOptionType.MultipleValue);
            var internalEndPointOption = cmdApp.Option("-iep", "PrivateEndPoint. Default: <null>. Leave null if your server have only one public IP. Used to find the certificate", CommandOptionType.SingleValue);
            var maxClientOption = cmdApp.Option("-maxClient", "MaximumClient. Default: 2", CommandOptionType.SingleValue);

            cmdApp.OnExecuteAsync(async _ =>
            {
                var accessServer = _fileAccessServer;
                var publicEndPoints = publicEndPointOption.Values.Count == 0 
                    ? defaultPublicEps.ToArray()
                    : publicEndPointOption.Values.Select(x => IPEndPoint.Parse(x!)).ToArray();

                var internalEndPoint = internalEndPointOption.HasValue()
                    ? IPEndPoint.Parse(internalEndPointOption.Value()!)
                    : null;

                var accessItem = accessServer.AccessItem_Create(
                    tokenName: nameOption.HasValue() ? nameOption.Value() : null,
                    publicEndPoints: publicEndPoints,
                    privateEndPoint: internalEndPoint,
                    maxClientCount: maxClientOption.HasValue() ? int.Parse(maxClientOption.Value()!) : 2);

                Console.WriteLine("The following token has been generated: ");
                await PrintToken(accessItem.Token.TokenId);
                Console.WriteLine($"Store Token Count: {accessServer.AccessItem_LoadAll().Length}");
                return 0;
            });
        }
    }
}