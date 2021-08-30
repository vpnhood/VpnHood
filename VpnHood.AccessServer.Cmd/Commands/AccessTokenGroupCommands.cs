using McMaster.Extensions.CommandLineUtils;
using VpnHood.AccessServer.Cmd.Apis;

namespace VpnHood.AccessServer.Cmd.Commands
{
    internal static class AccessTokenGroupCommands
    {
        private static AppSettings AppSettings => Program.AppSettings;
        public static void AddCommands(CommandLineApplication cmdApp)
        {
            cmdApp.Command("accessTokenGroups", MainCommands);
        }

        private static void MainCommands(CommandLineApplication cmdApp)
        {
            cmdApp.Command("create", Create);
        }

        private static void Create(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Create new accessTokenGroup";
            var nameArg = cmdApp.Argument("name", "Symmetric key in base64.").IsRequired();
            var makeDefaultOptions = cmdApp.Option("-makeDefault", "default: false", CommandOptionType.NoValue);

            cmdApp.OnExecute(() =>
            {
                AccessTokenGroupController accessTokenGroupController = new();
                var res = accessTokenGroupController.AccessTokenGroupsPOSTAsync(
                    AppSettings.ProjectId,
                    nameArg.Value,
                    makeDefaultOptions.HasValue()).Result;

                Program.PrintResult(res);
            });
        }

    }
}