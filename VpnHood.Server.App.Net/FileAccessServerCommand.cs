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

            var tokenIdArg = cmdApp.Argument("tokenId", "tokenId (Guid) or tokenSupportId (id) to print");
            cmdApp.OnExecuteAsync(async (cancellationToken) =>
            {
                if (!Guid.TryParse(tokenIdArg.Value, out var tokenId) && tokenIdArg.Value != null)
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

                await PrintToken(tokenId);
                return 0;
            });
        }

        private async Task PrintToken(Guid tokenId)
        {
            var accessItem = await _fileAccessServer.AccessItem_Read(tokenId);
            if (accessItem == null) throw new KeyNotFoundException($"Token does not exist! tokenId: {tokenId}");
            var access = await _fileAccessServer.GetAccess(new() { TokenId = tokenId, ClientInfo = new() { ClientId = Guid.Empty } });
            var serverAuthority = accessItem.Token.ServerAuthority + (accessItem.Token.IsValidServerAuthority ? "" : " (Fake)");
            
            Console.WriteLine();
            Console.WriteLine($"Access Details:");
            Console.WriteLine(JsonSerializer.Serialize(access, new JsonSerializerOptions() { WriteIndented = true }));
            Console.WriteLine();
            Console.WriteLine($"SupportId: {accessItem.Token.SupportId}");
            Console.WriteLine($"ServerEndPoint: {accessItem.Token.ServerEndPoint}");
            Console.WriteLine($"ServerAuthority: {serverAuthority}");
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
            var publicIpAddress = Util.GetPublicIpAddress().Result;
            cmdApp.Description = "Generate a token";
            var nameOption = cmdApp.Option("-name", $"TokenName. Default: <NoName>", CommandOptionType.SingleValue);
            var publicEndPointOption = cmdApp.Option("-ep", $"PublicEndPoint. Default: {publicIpAddress}:{AppSettings.EndPoint.Port}", CommandOptionType.SingleValue);
            var internalEndPointOption = cmdApp.Option("-iep", $"InternalEndPoint. Default: <null>. Leave null if your server have only one public IP", CommandOptionType.SingleValue);
            var maxClientOption = cmdApp.Option("-maxClient", $"MaximumClient. Default: 2", CommandOptionType.SingleValue);

            cmdApp.OnExecuteAsync(async (cancellationToken) =>
            {
                var accessServer = _fileAccessServer;
                var publicEndPoint = publicEndPointOption.HasValue() ? IPEndPoint.Parse(publicEndPointOption.Value()!) : AppSettings.EndPoint;
                var internalEndPoint = internalEndPointOption.HasValue() ? IPEndPoint.Parse(internalEndPointOption.Value()!) : null;
                if (publicEndPoint.Port == 0) publicEndPoint.Port = AppSettings.EndPoint.Port; //set default port
                if (internalEndPoint != null && internalEndPoint.Port == 0) internalEndPoint.Port = AppSettings.EndPoint.Port; //set default port

                var accessItem = accessServer.CreateAccessItem(
                    tokenName: nameOption.HasValue() ? nameOption.Value() : null,
                    publicEndPoint: publicEndPoint,
                    internalEndPoint: internalEndPoint,
                    maxClientCount: maxClientOption.HasValue() ? int.Parse(maxClientOption.Value()!) : 2);

                Console.WriteLine($"The following token has been generated: ");
                await PrintToken(accessItem.Token.TokenId);
                Console.WriteLine($"Store Token Count: {accessServer.GetAllTokenIds().Length}");
                return 0;
            });
        }
    }
}
