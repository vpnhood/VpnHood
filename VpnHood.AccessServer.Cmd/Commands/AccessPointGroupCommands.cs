using System;
using System.IO;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using VpnHood.AccessServer.Cmd.Apis;

namespace VpnHood.AccessServer.Cmd.Commands
{
    internal static class AccessPointGroupCommands
    {
        private static AppSettings AppSettings => Program.AppSettings;
        public static void AddCommands(CommandLineApplication cmdApp)
        {
            cmdApp.Command("accessPointGroups", MainCommands);
        }

        private static void MainCommands(CommandLineApplication cmdApp)
        {
            cmdApp.Command("create", Create);
            cmdApp.Command("list", List);
        }

        private static void List(CommandLineApplication cmdApp)
        {
            cmdApp.OnExecuteAsync(async ct =>
            {
                AccessPointGroupController accessPointGroupController = new();
                var res = await accessPointGroupController.AccessPointGroupsAllAsync(AppSettings.ProjectId, ct);
                Program.PrintResult(res);
            });
        }

        private static void Create(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Create new accessPointGroup";
            var nameArg = cmdApp.Argument("name", "").IsRequired();
            var certificateIdOption = cmdApp.Option("-certificateId", "default: new certificate will be created", CommandOptionType.SingleValue);
            var makeDefaultOptions = cmdApp.Option("-makeDefault", "default: false", CommandOptionType.NoValue);

            cmdApp.OnExecuteAsync(async (cancellationToken) =>
            {
                AccessPointGroupController accessPointGroupController = new();
                var res = await accessPointGroupController.AccessPointGroupsPOSTAsync(
                    AppSettings.ProjectId,
                    new AccessPointGroupCreateParams
                    {
                        AccessPointGroupName = nameArg.Value,
                        CertificateId = certificateIdOption.HasValue() ? Guid.Parse(certificateIdOption.Value()!) : null,
                        MakeDefault = makeDefaultOptions.HasValue()
                    }, cancellationToken);

                Program.PrintResult(res);
            });
        }

    }
}