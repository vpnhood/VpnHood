using McMaster.Extensions.CommandLineUtils;
using System;
using VpnHood.AccessServer.Apis;

namespace VpnHood.AccessServer.Cmd
{
    static class ServerEndPointCmd
    {
        public static void AddCommand(CommandLineApplication cmdApp)
        {
            cmdApp.Command("serverEndPoints", MainCommand);
        }

        private static void MainCommand(CommandLineApplication cmdApp)
        {
            cmdApp.Command("create", Create);
        }

        private static void Create(CommandLineApplication cmdApp)
        {
            var publicEndPointArg = cmdApp.Argument("publicEndPoint", null).IsRequired();
            var subjectName = cmdApp.Option("-subjectName", null, CommandOptionType.SingleValue);
            var accessTokenGroupId = cmdApp.Option("-accessTokenGroupId", null, CommandOptionType.SingleValue);
            var makeDefaultOptions = cmdApp.Option("-makeDefault", "null", CommandOptionType.NoValue);

            cmdApp.OnExecute(() =>
            {
                ServerEndPointClient serverEndPointClient = new();
                var res = serverEndPointClient.ServerEndpointsPOSTAsync(
                    projectId: Program.AppSettings.ProjectId,
                    publicEndPoint: publicEndPointArg.Value,
                    subjectName: subjectName.HasValue() ? subjectName.Value() : null,
                    accessTokenGroupId: accessTokenGroupId.HasValue() ? Guid.Parse(accessTokenGroupId.Value()) : null,
                    makeDefault: makeDefaultOptions.HasValue()).Result;

                Program.PrintResult(res);
            });
        }
    }
}
