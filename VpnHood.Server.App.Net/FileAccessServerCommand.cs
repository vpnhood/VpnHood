using McMaster.Extensions.CommandLineUtils;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Common;
using VpnHood.Server.AccessServers;

namespace VpnHood.Server.App
{
    public class FileAccessServerCommand
    {
        private readonly FileAccessServer _fileAccessServer;
        private static AppSettings AppSettings => ServerApp.Instance.AppSettings;

        public FileAccessServerCommand(FileAccessServer fileAccessServer)
        {
            _fileAccessServer = fileAccessServer;
        }

        public void AddCommands(CommandLineApplication cmdApp)
        {
            cmdApp.Command("print", PrintToken);
            cmdApp.Command("gen", GenerateToken);
        }

        private void PrintToken(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Print a token";

            var tokenIdArg = cmdApp.Argument("tokenId", "tokenId to print");
            cmdApp.OnExecuteAsync(async (cancellationToken) =>
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
            
            Console.WriteLine();
            Console.WriteLine($"Access Details:");
            Console.WriteLine(JsonSerializer.Serialize(accessItem.AccessUsage, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine();
            Console.WriteLine($"{nameof(Token.SupportId)}: {accessItem.Token.SupportId}");
            Console.WriteLine($"{nameof(Token.HostEndPoint)}: {accessItem.Token.HostEndPoint}");
            Console.WriteLine($"{nameof(Token.HostName)}: {hostName}");
            Console.WriteLine($"{nameof(Token.HostPort)}: {accessItem.Token.HostPort}");
            Console.WriteLine($"TokenUpdateUrl: {accessItem.Token.Url}");
            Console.WriteLine($"---");
            
            Console.WriteLine();
            Console.WriteLine("AccessKey:");
            Console.WriteLine(accessItem.Token.ToAccessKey());
            Console.WriteLine($"---");
            Console.WriteLine();

        }

        private void GenerateToken(CommandLineApplication cmdApp)
        {
            // prepare default public ip
            var publicIp = Util.GetPublicIpAddress().Result;
            IPEndPoint? defaultEp = publicIp!=null ? new IPEndPoint(publicIp, AppSettings.EndPoint.Port) : null;
            if (defaultEp == null && AppSettings.EndPoint.Address != IPAddress.Any) defaultEp = AppSettings.EndPoint;
            var publicEndPointDesc = defaultEp != null ? $"PublicEndPoint. Default: {defaultEp}" : $"PublicEndPoint. *Required";

            cmdApp.Description = "Generate a token";
            var nameOption = cmdApp.Option("-name", $"TokenName. Default: <NoName>", CommandOptionType.SingleValue);
            var publicEndPointOption = cmdApp.Option("-ep", publicEndPointDesc, CommandOptionType.SingleValue);
            var internalEndPointOption = cmdApp.Option("-iep", $"InternalEndPoint. Default: <null>. Leave null if your server have only one public IP", CommandOptionType.SingleValue);
            var maxClientOption = cmdApp.Option("-maxClient", $"MaximumClient. Default: 2", CommandOptionType.SingleValue);

            // mark publicEndPointOption as required if could not find any defaultEp
            if (defaultEp == null)
                publicEndPointOption.IsRequired();

            cmdApp.OnExecuteAsync(async (cancellationToken) =>
            {
                var accessServer = _fileAccessServer;
                var publicEndPoint = publicEndPointOption.HasValue() ? IPEndPoint.Parse(publicEndPointOption.Value()!) : defaultEp!;
                var internalEndPoint = internalEndPointOption.HasValue() ? IPEndPoint.Parse(internalEndPointOption.Value()!) : null;
                if (publicEndPoint.Port == 0) publicEndPoint.Port = AppSettings.EndPoint.Port; //set default port
                if (internalEndPoint != null && internalEndPoint.Port == 0) internalEndPoint.Port = AppSettings.EndPoint.Port; //set default port

                var accessItem = accessServer.AccessItem_Create(
                    tokenName: nameOption.HasValue() ? nameOption.Value() : null,
                    publicEndPoint: publicEndPoint,
                    internalEndPoint: internalEndPoint,
                    maxClientCount: maxClientOption.HasValue() ? int.Parse(maxClientOption.Value()!) : 2);

                Console.WriteLine($"The following token has been generated: ");
                await PrintToken(accessItem.Token.TokenId);
                Console.WriteLine($"Store Token Count: {accessServer.AccessItem_LoadAll().Length}");
                return 0;
            });
        }
    }
}
