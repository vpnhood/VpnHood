using System;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using VpnHood.AccessServer.Cmd.Apis;

namespace VpnHood.AccessServer.Cmd.Commands
{
    internal static class AccessTokenCommands
    {
        private static AppSettings AppSettings => Program.AppSettings;
        public static void AddCommands(CommandLineApplication cmdApp)
        {
            cmdApp.Command("accessTokens", MainCommands);
        }

        private static void MainCommands(CommandLineApplication cmdApp)
        {
            cmdApp.Command("create", Create);
            cmdApp.Command("get", Get);
            cmdApp.Command("list", List);
            cmdApp.Command("update", Update);
        }


        private static void List(CommandLineApplication cmdApp)
        {
            var groupIdOption = cmdApp.Option("-groupId", "Default: Default groupId", CommandOptionType.SingleValue);

            cmdApp.OnExecuteAsync(async ct =>
            {
                AccessTokenController accessTokenController = new();
                var items = await accessTokenController.ListAsync(
                    AppSettings.ProjectId, 
                    groupIdOption.HasValue() ? Guid.Parse(groupIdOption.Value()!) : null, cancellationToken: ct);

                Console.WriteLine("Listing...");
                foreach (var item in items)
                    Console.WriteLine($"SupportCode: {item.AccessToken.SupportCode}, Id: {item.AccessToken.AccessTokenId}, Name: {item.AccessToken.AccessTokenName}");
            });
        }

        private static void Create(CommandLineApplication cmdApp)
        {
            const int defaultTraffic = 0;
            const int defaultMaxClient = 3;
            const int defaultLifetime = 0;

            var groupIdOption = cmdApp.Option("-groupId", "Default: Default groupId", CommandOptionType.SingleValue);
            var nameOption = cmdApp.Option("-name", "Default: <null>", CommandOptionType.SingleValue);
            var isPublicOption = cmdApp.Option("-public", "Default: create a private key", CommandOptionType.NoValue);
            var maxTrafficOption = cmdApp.Option("-maxTraffic", $"in MB, Default: {defaultTraffic} MB",
                CommandOptionType.SingleValue);
            var maxClientOption = cmdApp.Option("-maxClient", $"Maximum concurrent client, Default: {defaultMaxClient}",
                CommandOptionType.SingleValue);
            var lifetimeOption = cmdApp.Option("-lifetime",
                $"The count of working days after first connection, 0 means no expiration time, Default: {defaultLifetime}",
                CommandOptionType.SingleValue);
            var urlOption = cmdApp.Option("-url", "Default: <null>", CommandOptionType.SingleValue);

            cmdApp.OnExecuteAsync(async ct =>
            {
                AccessTokenController accessTokenController = new();
                var accessToken = await accessTokenController.AccessTokensPOSTAsync(AppSettings.ProjectId,
                    new AccessTokenCreateParams
                    {
                        AccessPointGroupId = groupIdOption.HasValue() ? Guid.Parse(groupIdOption.Value()!) : null,
                        AccessTokenName = nameOption.HasValue() ? nameOption.Value()! : null,
                        IsPublic = isPublicOption.HasValue(),
                        Lifetime = lifetimeOption.HasValue() ? int.Parse(lifetimeOption.Value()!) : defaultLifetime,
                        MaxClient = maxClientOption.HasValue() ? int.Parse(maxClientOption.Value()!) : defaultMaxClient,
                        MaxTraffic =
                            maxTrafficOption.HasValue() ? int.Parse(maxTrafficOption.Value()!) : defaultTraffic,
                        Url = urlOption.HasValue() ? urlOption.Value() : null
                    }, ct);

                var accessKey =
                    await accessTokenController.AccessKeyAsync(AppSettings.ProjectId, accessToken.AccessTokenId, ct);
                Console.WriteLine($"AccessKey\n{accessKey.Key}");
            });
        }

        private static void Update(CommandLineApplication cmdApp)
        {
            var accessTokenIdArg = cmdApp.Argument("accessTokenId", "").IsRequired();
            var groupIdOption = cmdApp.Option("-groupId", "", CommandOptionType.SingleValue);
            var nameOption = cmdApp.Option("-name", "", CommandOptionType.SingleValue);
            var maxTrafficOption = cmdApp.Option("-maxTraffic", "in MB, 0 means no limit", CommandOptionType.SingleValue);
            var maxClientOption = cmdApp.Option("-maxClient", "in MB, 0 means no limit",CommandOptionType.SingleValue);
            var lifetimeOption = cmdApp.Option("-lifetime", "The count of working days after first connection, 0 means no expiration time", CommandOptionType.SingleValue);
            var tokenUrlOption = cmdApp.Option("-url", "", CommandOptionType.SingleValue);
            cmdApp.OnExecuteAsync(async ct =>
            {
                AccessTokenController accessTokenController = new();
                var accessToken = await accessTokenController.AccessTokensPUTAsync(AppSettings.ProjectId, Guid.Parse(accessTokenIdArg.Value!),
                    new AccessTokenUpdateParams
                    {
                        AccessPointGroupId = groupIdOption.HasValue() ? new GuidWise{ Value = Guid.Parse(groupIdOption.Value()!) } : null,
                        AccessTokenName = nameOption.HasValue() ? new StringWise { Value = nameOption.Value()! } : null,
                        Lifetime = lifetimeOption.HasValue() ? new Int32Wise { Value = int.Parse(lifetimeOption.Value()!) } : null,
                        MaxClient = maxClientOption.HasValue() ? new Int32Wise { Value = int.Parse(maxClientOption.Value()!) * 1000000 } : null,
                        MaxTraffic = maxTrafficOption.HasValue() ? new Int64Wise { Value = long.Parse(maxTrafficOption.Value()!) * 1000000 } : null,
                        Url = tokenUrlOption.HasValue() ? new StringWise{Value =  tokenUrlOption.Value()} : null
                    }, ct);

                var accessKey = await accessTokenController.AccessKeyAsync(AppSettings.ProjectId, accessToken.AccessTokenId, ct);
                Program.PrintResult(accessToken);
                Program.PrintResult(accessKey);
            });
        }

        private static void Get(CommandLineApplication cmdApp)
        {
            var accessTokenIdArg =
                cmdApp.Argument("accessTokenId", "").IsRequired();

            cmdApp.OnExecuteAsync(async ct =>
            {
                var accessTokenId = Guid.Parse(accessTokenIdArg.Value!);

                AccessTokenController accessTokenController = new();
                var accessTokenTask = accessTokenController.AccessTokensGETAsync(AppSettings.ProjectId, accessTokenId, ct);
                var accessKeyTask = accessTokenController.AccessKeyAsync(AppSettings.ProjectId, accessTokenId, ct);
                await Task.WhenAll(accessTokenTask, accessKeyTask);

                Console.WriteLine($"{Program.FormatResult(await accessTokenTask)}");
                Console.WriteLine($"AccessKey\n{(await accessKeyTask).Key}");
            });

                        
        }
    }
}