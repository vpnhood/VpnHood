using System.Net;
using System.Text.Json;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using VpnHood.Common;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Server.Access.Managers.File;

namespace VpnHood.Server.App;

public class FileAccessManagerCommand
{
    private readonly FileAccessManager _fileAccessManager;

    public FileAccessManagerCommand(FileAccessManager fileAccessManager)
    {
        _fileAccessManager = fileAccessManager;
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
        cmdApp.OnExecuteAsync(async _ =>
        {
            await PrintToken(Guid.Parse(tokenIdArg.Value!));
            return 0;
        });
    }

    private async Task PrintToken(Guid tokenId)
    {
        var accessItem = await _fileAccessManager.AccessItem_Read(tokenId);
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

    private IPEndPoint[] GetDefaultPublicEndPoints()
    {
        var publicIps = IPAddressUtil.GetPublicIpAddresses().Result;
        var defaultPublicEps = new List<IPEndPoint>();
        var allListenerPorts = _fileAccessManager.ServerConfig.TcpEndPointsValue
            .Select(x => x.Port)
            .Distinct();

        foreach (var port in allListenerPorts)
            defaultPublicEps.AddRange(publicIps.Select(x => new IPEndPoint(x, port)));

        return defaultPublicEps.ToArray();
    }

    private void GenerateToken(CommandLineApplication cmdApp)
    {
        var accessManager = _fileAccessManager;

        cmdApp.Description = "Generate a token";
        var nameOption = cmdApp.Option("-name", "TokenName. Default: <NoName>", CommandOptionType.SingleValue);
        var useDomainOption = cmdApp.Option("-domain", "Use Certificate's domain name for endpoint. Default: Use public IP", CommandOptionType.NoValue);
        var publicEndPointOption = cmdApp.Option("-ep", "PublicEndPoints. Default: Server-Public-IP", CommandOptionType.SingleValue);
        var maxClientOption = cmdApp.Option("-maxClient", "MaximumClient. Default: 2", CommandOptionType.SingleValue);
        var maxTrafficOptions = cmdApp.Option("-maxTraffic", "MaximumTraffic in MB. Default: unlimited", CommandOptionType.SingleValue);
        var expirationTimeOption = cmdApp.Option("-expire", "ExpirationTime. Default: Never Expire. Format: 2030/01/25", CommandOptionType.SingleValue);

        cmdApp.OnExecuteAsync(async _ =>
        {
            if (useDomainOption.HasValue() && publicEndPointOption.HasValue())
                throw new InvalidOperationException("Can not set -domain and -ep options together.");

            var publicEndPoints = Array.Empty<IPEndPoint>();
            var tcpEndPoints = accessManager.ServerConfig.TcpEndPointsValue;
            if (!useDomainOption.HasValue())
            {
                publicEndPoints  = publicEndPointOption.HasValue()
                    ? publicEndPointOption.Value()!.Split(",").Select(x => IPEndPoint.Parse(x.Trim())).ToArray()
                    : GetDefaultPublicEndPoints();

                // set default ports
                foreach (var item in publicEndPoints.Where(x => x.Port == 0))
                {
                    var bestEp = tcpEndPoints.FirstOrDefault(x => x.AddressFamily == item.AddressFamily);
                    item.Port = bestEp?.Port ?? 443;
                }

                // throw error if could not find any public endpoint
                if (publicEndPoints.Length == 0)
                {
                    VhLogger.Instance.LogError(
                        "Could not find any public IP to add to the token. Check -ep option.");
                    return -1;
                }
            }

            var accessItem = accessManager.AccessItem_Create(
                tokenName: nameOption.HasValue() ? nameOption.Value() : null,
                publicEndPoints: publicEndPoints,
                maxClientCount: maxClientOption.HasValue() ? int.Parse(maxClientOption.Value()!) : 2,
                isValidHostName: useDomainOption.HasValue(),
                hostPort: tcpEndPoints.FirstOrDefault()?.Port ?? 443,
                maxTrafficByteCount: maxTrafficOptions.HasValue() ? int.Parse(maxTrafficOptions.Value()!) * 1_000_000 : 0,
                expirationTime: expirationTimeOption.HasValue() ? DateTime.Parse(expirationTimeOption.Value()!) : null
                );

            Console.WriteLine("The following token has been generated: ");
            await PrintToken(accessItem.Token.TokenId);
            Console.WriteLine($"Store Token Count: {accessManager.AccessItem_LoadAll().Length}");
            return 0;
        });
    }
}