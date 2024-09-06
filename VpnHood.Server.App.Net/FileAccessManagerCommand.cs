using System.Text.Json;
using McMaster.Extensions.CommandLineUtils;
using VpnHood.Common;
using VpnHood.Common.Utils;
using VpnHood.Server.Access.Managers.File;

namespace VpnHood.Server.App;

public class FileAccessManagerCommand(FileAccessManager fileAccessManager)
{
    public void AddCommands(CommandLineApplication cmdApp)
    {
        cmdApp.Command("print", PrintToken);
        cmdApp.Command("gen", GenerateToken);
    }

    private void PrintToken(CommandLineApplication cmdApp)
    {
        cmdApp.Description = "Print a token";

        var tokenIdArg = cmdApp.Argument("tokenId", "tokenId to print");
        cmdApp.OnExecuteAsync(async _ => {
            await PrintToken(tokenIdArg.Value!).VhConfigureAwait();
            return 0;
        });
    }

    private async Task PrintToken(string tokenId)
    {
        var accessItem = await fileAccessManager.AccessItem_Read(tokenId).VhConfigureAwait();
        if (accessItem == null) throw new KeyNotFoundException($"Token does not exist! tokenId: {tokenId}");
        var hostName = accessItem.Token.ServerToken.HostName +
                       (accessItem.Token.ServerToken.IsValidHostName ? "" : " (Fake)");
        var endPoints = accessItem.Token.ServerToken.HostEndPoints?.Select(x => x.ToString()) ?? Array.Empty<string>();

        Console.WriteLine();
        Console.WriteLine("Access Details:");
        Console.WriteLine(JsonSerializer.Serialize(accessItem.AccessUsage, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine();
        Console.WriteLine($"{nameof(Token.SupportId)}: {accessItem.Token.SupportId}");
        Console.WriteLine($"{nameof(ServerToken.HostEndPoints)}: {string.Join(",", endPoints)}");
        Console.WriteLine($"{nameof(ServerToken.HostName)}: {hostName}");
        Console.WriteLine($"{nameof(ServerToken.HostPort)}: {accessItem.Token.ServerToken.HostPort}");
        Console.WriteLine("TokenUpdateUrls: " + (VhUtil.IsNullOrEmpty(accessItem.Token.ServerToken.Urls) ? "NotSet" : ""));
        foreach (var url in accessItem.Token.ServerToken.Urls ?? [])
            Console.WriteLine($"\t Url: {url}");
        Console.WriteLine("---");

        Console.WriteLine();
        Console.WriteLine("AccessKey:");
        Console.WriteLine();
        Console.WriteLine(accessItem.Token.ToAccessKey());
        Console.WriteLine("---");
        Console.WriteLine();
    }

    private void GenerateToken(CommandLineApplication cmdApp)
    {
        var accessManager = fileAccessManager;

        cmdApp.Description = "Generate a token";
        var nameOption = cmdApp.Option("-name", "TokenName. Default: <NoName>", CommandOptionType.SingleValue);
        var maxClientOption = cmdApp.Option("-maxClient", "MaximumClient. Default: 2", CommandOptionType.SingleValue);
        var maxTrafficOptions = cmdApp.Option("-maxTraffic", "MaximumTraffic in MB. Default: unlimited",
            CommandOptionType.SingleValue);
        var expirationTimeOption = cmdApp.Option("-expire", "ExpirationTime. Default: Never Expire. Format: 2030/01/25",
            CommandOptionType.SingleValue);

        cmdApp.OnExecuteAsync(async _ => {
            var accessItem = accessManager.AccessItem_Create(
                tokenName: nameOption.HasValue() ? nameOption.Value() : null,
                maxClientCount: maxClientOption.HasValue() ? int.Parse(maxClientOption.Value()!) : 2,
                maxTrafficByteCount: maxTrafficOptions.HasValue()
                    ? int.Parse(maxTrafficOptions.Value()!) * 1_000_000
                    : 0,
                expirationTime: expirationTimeOption.HasValue() ? DateTime.Parse(expirationTimeOption.Value()!) : null
            );

            Console.WriteLine("The following token has been generated: ");
            await PrintToken(accessItem.Token.TokenId).VhConfigureAwait();
            Console.WriteLine($"Store Token Count: {accessManager.AccessItem_Count()}");
            return 0;
        });
    }
}