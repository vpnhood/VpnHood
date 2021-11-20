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
            var accessPointId = cmdApp.Argument("accessPointId", "").IsRequired();
            cmdApp.OnExecuteAsync(async ct =>
            {
                AccessPointController accessPointController = new();
                await accessPointController.AccessPointsDELETEAsync(AppSettings.ProjectId, Guid.Parse(accessPointId.Value!), ct);
                Console.WriteLine("Deleted!");
            });
        }

        private static void Create(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Create a Certificate and add it to the server.";
            var serverIdArg = cmdApp.Argument("serverId", "").IsRequired();
            var ipAddressArg = cmdApp.Argument("ipAddress", "").IsRequired();
            var isListenOption = cmdApp.Option("isListen", "", CommandOptionType.NoValue);
            var groupIdOption = cmdApp.Option("-groupId", "Default: null", CommandOptionType.SingleValue).IsRequired();
            var modeOption = cmdApp.Option("-mode", "Default: public, private | public | publicInToken", CommandOptionType.SingleValue);
            var tcpPortOption = cmdApp.Option("-tcpPort", "Default: 443", CommandOptionType.SingleValue);
            var udpPortOption = cmdApp.Option("-udpPort", "Default: auto", CommandOptionType.SingleValue);

            cmdApp.OnExecuteAsync(async ct =>
            {
                var accessPointController = new AccessPointController();
                await accessPointController.AccessPointsPOSTAsync(
                    AppSettings.ProjectId,
                    Guid.Parse(serverIdArg.Value!),
                    new AccessPointCreateParams
                    {
                        IpAddress = ipAddressArg.Value,
                        AccessPointGroupId = Guid.Parse(groupIdOption.Value()!),
                        IsListen = isListenOption.HasValue(),
                        AccessPointMode = Enum.Parse<AccessPointMode>(modeOption.HasValue() ? modeOption.Value()! : "Public", true),
                        TcpPort = tcpPortOption.HasValue() ? int.Parse(tcpPortOption.Value()!) : 443,
                        UdpPort = udpPortOption.HasValue() ? int.Parse(udpPortOption.Value()!) : 0
                    }, ct);

                Console.WriteLine("Created!");
            });
        }

        private static void Update(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Update an AccessPoint";
            var accessPointIdArg = cmdApp.Argument("accessPointId", "").IsRequired();
            var ipAddressOptions = cmdApp.Option("ipAddress", "", CommandOptionType.SingleValue).IsRequired();
            var isListenOption = cmdApp.Option("isListen", "", CommandOptionType.SingleValue);
            var groupIdOption = cmdApp.Option("-groupId", "", CommandOptionType.SingleValue).IsRequired();
            var modeOption = cmdApp.Option("-mode", "private|public|publicInToken", CommandOptionType.SingleValue);
            var tcpPortOption = cmdApp.Option("-tcpPort", "", CommandOptionType.SingleValue);
            var udpPortOption = cmdApp.Option("-udpPort", "0 for auto", CommandOptionType.SingleValue);

            cmdApp.OnExecuteAsync(async ct =>
            {
                var accessPointController = new AccessPointController();
                await accessPointController.AccessPointsPATCHAsync(AppSettings.ProjectId,
                    Guid.Parse(accessPointIdArg.Value!),
                    new AccessPointUpdateParams
                    {
                        AccessPointGroupId = groupIdOption.HasValue()
                            ? new GuidWise { Value = Guid.Parse(groupIdOption.Value()!) }
                            : null,
                        IpAddress = ipAddressOptions.HasValue()
                            ? new StringWise { Value = ipAddressOptions.Value()! }
                            : null,
                        IsListen = isListenOption.HasValue()
                            ? new BooleanWise { Value = bool.Parse(isListenOption.Value()!) }
                            : null,
                        AccessPointMode = modeOption.HasValue()
                            ? new AccessPointModeWise { Value = Enum.Parse<AccessPointMode>(modeOption.Value()!) }
                            : null,
                        TcpPort = tcpPortOption.HasValue()
                            ? new Int32Wise { Value = int.Parse(tcpPortOption.Value()!) }
                            : null,
                        UdpPort = udpPortOption.HasValue()
                            ? new Int32Wise { Value = int.Parse(udpPortOption.Value()!) }
                            : null,
                    }, ct);

                Console.WriteLine("Updated!");
            });
        }
    }
}