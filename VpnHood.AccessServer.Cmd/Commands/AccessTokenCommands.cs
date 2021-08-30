using System;
using System.Linq;
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
            cmdApp.Command("get", GetAccessKey);
            cmdApp.Command("list", List);
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
            var tokenUrlOption = cmdApp.Option("-tokenUrl", "Default: <null>", CommandOptionType.SingleValue);

            cmdApp.OnExecuteAsync(async ct =>
            {
                AccessTokenController accessTokenController = new();
                var accessToken = await accessTokenController.AccessTokensPOSTAsync(AppSettings.ProjectId,
                    new AccessTokenCreateParams
                    {
                        AccessTokenGroupId = groupIdOption.HasValue() ? Guid.Parse(groupIdOption.Value()!) : null,
                        AccessTokenName = nameOption.HasValue() ? nameOption.Value()! : null,
                        IsPublic = isPublicOption.HasValue(),
                        Lifetime = lifetimeOption.HasValue() ? int.Parse(lifetimeOption.Value()!) : defaultLifetime,
                        MaxClient = maxClientOption.HasValue() ? int.Parse(maxClientOption.Value()!) : defaultMaxClient,
                        MaxTraffic =
                            maxTrafficOption.HasValue() ? int.Parse(maxTrafficOption.Value()!) : defaultTraffic,
                        Url = tokenUrlOption.HasValue() ? tokenUrlOption.Value() : null
                    }, ct);

                var accessKey =
                    await accessTokenController.AccessKeyAsync(AppSettings.ProjectId, accessToken.AccessTokenId, ct);
                Console.WriteLine($"AccessKey\n{accessKey.Key}");
            });
        }

        private static void GetAccessKey(CommandLineApplication cmdApp)
        {
            var accessTokenIdOption =
                cmdApp.Option("-tid|--accessTokenId", "* Required", CommandOptionType.SingleValue);

            cmdApp.OnExecute(() =>
            {
                if (!accessTokenIdOption.HasValue()) throw new ArgumentNullException(accessTokenIdOption.LongName);
                var accessTokenId = Guid.Parse(accessTokenIdOption.Value()!);

                AccessTokenController accessTokenController = new();
                var accessKey = accessTokenController.AccessKeyAsync(AppSettings.ProjectId, accessTokenId).Result;
                Console.WriteLine($"AccessKey\n{accessKey.Key}");
            });
        }
    }
}