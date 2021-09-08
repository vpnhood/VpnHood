using System;
using McMaster.Extensions.CommandLineUtils;
using VpnHood.AccessServer.Cmd.Apis;

namespace VpnHood.AccessServer.Cmd.Commands
{
    internal static class AccessPointCommands
    {
        private static AppSettings AppSettings => Program.AppSettings;

        public static void AddCommands(CommandLineApplication cmdApp)
        {
            cmdApp.Command("accessPoints", MainCommand);
        }

        private static void MainCommand(CommandLineApplication cmdApp)
        {
            cmdApp.Command("create", Create);
            cmdApp.Command("update", Update);
            cmdApp.Command("delete", Delete);
        }

        private static void Delete(CommandLineApplication cmdApp)
        {
            var publicEndPointArg = cmdApp.Argument("publicEndPoint", "").IsRequired();
            cmdApp.OnExecuteAsync(async ct =>
            {
                AccessPointController accessPointController = new();
                await accessPointController.AccessPointsDELETEAsync(AppSettings.ProjectId,
                    publicEndPointArg.Value!, ct);
                Console.WriteLine("Deleted!");
            });
        }

        private static void Create(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Create a Certificate and add it to the server.";
            var publicEndPointArg = cmdApp.Argument("publicEndPoint", "").IsRequired();
            var privateEndPointOptions = cmdApp.Option("-privateEndPoint", "Private EndPoint that Public is mapped to. Default: null", CommandOptionType.SingleValue);
            var groupIdOption = cmdApp.Option("-groupId", "Default: Default groupId", CommandOptionType.SingleValue);
            var makeDefaultOption = cmdApp.Option("-makeDefault", "default: not set", CommandOptionType.NoValue);

            cmdApp.OnExecuteAsync(async ct =>
            {
                AccessPointController accessPointController = new();
                await accessPointController.AccessPointsPOSTAsync(AppSettings.ProjectId,
                    new AccessPointCreateParams
                    {
                        PublicEndPoint = publicEndPointArg.Value,
                        PrivateEndPoint = privateEndPointOptions.HasValue() ? privateEndPointOptions.Value() : null,
                        AccessPointGroupId = groupIdOption.HasValue() ? Guid.Parse(groupIdOption.Value()!) : null,
                        MakeDefault = makeDefaultOption.HasValue()
                    }, ct);

                Console.WriteLine("Created!");
            });
        }

        private static void Update(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Update an EndPoint";
            var publicEndPointArg = cmdApp.Argument("publicEndPoint", "").IsRequired();
            var privateEndPointOptions = cmdApp.Option("-privateEndPoint", "", CommandOptionType.SingleValue);
            var groupIdOption = cmdApp.Option("-groupId", "", CommandOptionType.SingleValue);
            var makeDefaultOption = cmdApp.Option("-makeDefault", "", CommandOptionType.NoValue);

            cmdApp.OnExecuteAsync(async ct =>
            {
                AccessPointController accessPointController = new();
                await accessPointController.AccessPointsPATCHAsync(AppSettings.ProjectId,
                    publicEndPointArg.Value!,
                    new AccessPointUpdateParams
                    {
                        MakeDefault = makeDefaultOption.HasValue()
                            ? new BooleanWise { Value = makeDefaultOption.HasValue() }
                            : null,
                        AccessPointGroupId = groupIdOption.HasValue()
                            ? new GuidWise { Value = Guid.Parse(groupIdOption.Value()!) }
                            : null,
                        PrivateEndPoint = privateEndPointOptions.HasValue()
                            ? new StringWise { Value = privateEndPointOptions.Value()! }
                            : null
                    }, ct);

                Console.WriteLine("Updated!");
            });
        }
    }
}