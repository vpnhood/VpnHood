using McMaster.Extensions.CommandLineUtils;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers;

namespace VpnHood.App.Server;

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
        var accessTokenData = await fileAccessManager.AccessTokenService.Get(tokenId).VhConfigureAwait();
        var token = fileAccessManager.GetToken(accessTokenData.AccessToken);
        if (accessTokenData == null) throw new KeyNotFoundException($"Token does not exist! tokenId: {tokenId}");
        var hostName = token.ServerToken.HostName +
                       (token.ServerToken.IsValidHostName ? "" : " (Fake)");
        var endPoints = token.ServerToken.HostEndPoints?.Select(x => x.ToString()) ?? Array.Empty<string>();

        Console.WriteLine();
        Console.WriteLine("--- Access Usage ---");
        Console.WriteLine();
        Console.WriteLine($"{nameof(accessTokenData.Usage.Sent)}: {VhUtil.FormatBytes(accessTokenData.Usage.Sent)}");
        Console.WriteLine($"{nameof(accessTokenData.Usage.Received)}: {VhUtil.FormatBytes(accessTokenData.Usage.Received)}");

        Console.WriteLine();
        Console.WriteLine("--- Access Token ---");
        Console.WriteLine();
        Console.WriteLine($"{nameof(ServerToken.HostEndPoints)}: {string.Join(",", endPoints)}");
        Console.WriteLine($"{nameof(ServerToken.HostName)}: {hostName}");
        Console.WriteLine($"{nameof(ServerToken.HostPort)}: {token.ServerToken.HostPort}");
        Console.WriteLine("TokenUpdateUrls: " + (VhUtil.IsNullOrEmpty(token.ServerToken.Urls) ? "NotSet" : ""));
        foreach (var url in token.ServerToken.Urls ?? [])
            Console.WriteLine($"\t Url: {url}");

        Console.WriteLine();
        Console.WriteLine("--- AccessKey ---");
        Console.WriteLine();
        Console.WriteLine(token.ToAccessKey());
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
            var accessToken = accessManager.AccessTokenService.Create(
                tokenName: nameOption.HasValue() ? nameOption.Value() : null,
                maxClientCount: maxClientOption.HasValue() ? int.Parse(maxClientOption.Value()!) : 2,
                maxTrafficByteCount: maxTrafficOptions.HasValue()
                    ? int.Parse(maxTrafficOptions.Value()!) * 1_000_000
                    : 0,
                expirationTime: expirationTimeOption.HasValue() ? DateTime.Parse(expirationTimeOption.Value()!) : null
            );

            Console.WriteLine("The following token has been generated: ");
            await PrintToken(accessToken.TokenId).VhConfigureAwait();
            Console.WriteLine($"Store Token Count: {accessManager.AccessTokenService.GetTotalCount()}");
            return 0;
        });
    }
}