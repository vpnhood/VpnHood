using System;
using McMaster.Extensions.CommandLineUtils;
using VpnHood.AccessServer.Apis;

namespace VpnHood.AccessServer.Cmd
{
    internal static class ServerEndPointCmd
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
            cmdApp.OnExecuteAsync(async ct =>
            {
                ServerEndPointClient serverEndPointClient = new();
                var res = await serverEndPointClient.ServerEndpointsPOSTAsync(
                    cancellationToken: ct,
                    projectId: Program.AppSettings.ProjectId,
                    publicEndPoint: publicEndPointArg.Value,
                    body: new ServerEndPointCreateParams
                    {
                        SubjectName = subjectName.HasValue() ? subjectName.Value() : null,
                        AccessTokenGroupId =
                            accessTokenGroupId.HasValue() ? Guid.Parse(accessTokenGroupId.Value()) : null,
                        MakeDefault = makeDefaultOptions.HasValue()
                    });

                Program.PrintResult(res);
            });
        }
    }
}