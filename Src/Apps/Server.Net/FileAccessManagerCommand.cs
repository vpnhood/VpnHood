using System.CommandLine;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server.Access.Managers.FileAccessManagement;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.App.Server;

public class FileAccessManagerCommand(FileAccessManager fileAccessManager)
{
    public void AddCommands(Command rootCommand)
    {
        rootCommand.Add(CreatePrintCommand());
        rootCommand.Add(CreateGenerateCommand());
    }

    private Command CreatePrintCommand()
    {
        var command = new Command("print", "Print a token");
        var tokenIdArg = new Argument<string>("tokenId") { Description = "tokenId to print" };
        command.Add(tokenIdArg);

        command.SetAction(async (parseResult, cancellationToken) => {
            var tokenId = parseResult.GetValue(tokenIdArg);
            ArgumentNullException.ThrowIfNull(tokenId);

            await PrintToken(tokenId, cancellationToken).Vhc();
        });
        return command;
    }

    private async Task PrintToken(string tokenId, CancellationToken cancellationToken)
    {
        var accessTokenData = await fileAccessManager.AccessTokenService.Get(tokenId, cancellationToken).Vhc();
        var token = fileAccessManager.GetToken(accessTokenData.AccessToken);
        if (accessTokenData == null) throw new KeyNotFoundException($"Token does not exist! tokenId: {tokenId}");
        var hostName = token.ServerToken.HostName +
                       (token.ServerToken.IsValidHostName ? "" : " (Fake)");
        var endPoints = token.ServerToken.HostEndPoints?.Select(x => x.ToString()) ?? [];

        Console.WriteLine();
        Console.WriteLine("--- Access Usage ---");
        Console.WriteLine();
        Console.WriteLine($"{nameof(accessTokenData.Usage.Sent)}: {VhUtils.FormatBytes(accessTokenData.Usage.Sent)}");
        Console.WriteLine(
            $"{nameof(accessTokenData.Usage.Received)}: {VhUtils.FormatBytes(accessTokenData.Usage.Received)}");

        Console.WriteLine();
        Console.WriteLine("--- Access Token ---");
        Console.WriteLine();
        Console.WriteLine($"{nameof(ServerToken.HostEndPoints)}: {string.Join(",", endPoints)}");
        Console.WriteLine($"{nameof(ServerToken.HostName)}: {hostName}");
        Console.WriteLine($"{nameof(ServerToken.HostPort)}: {token.ServerToken.HostPort}");
        Console.WriteLine("TokenUpdateUrls: " + (VhUtils.IsNullOrEmpty(token.ServerToken.Urls) ? "NotSet" : ""));
        foreach (var url in token.ServerToken.Urls ?? [])
            Console.WriteLine($"\t Url: {url}");

        Console.WriteLine();
        Console.WriteLine("--- AccessKey ---");
        Console.WriteLine();
        Console.WriteLine(token.ToAccessKey());
        Console.WriteLine();
    }

    private Command CreateGenerateCommand()
    {
        var command = new Command("gen", "Generate a token");
        var nameOption = new Option<string?>("-name") {
            Description = "TokenName. Default: <NoName>" 
        };
        var maxClientOption = new Option<int?>("-maxClient") {
            Description = "MaximumClient. Default: 2" 
        };
        var maxTrafficOptions = new Option<int?>("-maxTraffic") {
            Description = "MaximumTraffic in MB. Default: unlimited"
        };
        var expirationTimeOption = new Option<DateTime?>("-expire") {
            Description = "ExpirationTime. Default: Never Expire. Format: 2030/01/25"
        };

        command.Add(nameOption);
        command.Add(maxClientOption);
        command.Add(maxTrafficOptions);
        command.Add(expirationTimeOption);

        command.SetAction(async (parseResult, cancellationToken) => {
            var accessToken = fileAccessManager.AccessTokenService.Create(
                tokenName: parseResult.GetValue(nameOption),
                maxClientCount: parseResult.GetValue(maxClientOption) ?? 2,
                maxTrafficByteCount: (parseResult.GetValue(maxTrafficOptions) ?? 0) * 1_000_000,
                expirationTime: parseResult.GetValue(expirationTimeOption)
            );

            Console.WriteLine("The following token has been generated: ");
            await PrintToken(accessToken.TokenId, cancellationToken).Vhc();
            Console.WriteLine($"Store Token Count: {await fileAccessManager.AccessTokenService.GetTotalCount()}");
        });

        return command;
    }
}
