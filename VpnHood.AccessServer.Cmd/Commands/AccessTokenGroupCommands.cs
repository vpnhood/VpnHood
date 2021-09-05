using McMaster.Extensions.CommandLineUtils;
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
        }

        private static void Create(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Create new accessPointGroup";
            var nameArg = cmdApp.Argument("name", "Symmetric key in base64.").IsRequired();
            var makeDefaultOptions = cmdApp.Option("-makeDefault", "default: false", CommandOptionType.NoValue);

            cmdApp.OnExecute(() =>
            {
                AccessPointGroupController accessPointGroupController = new();
                var res = accessPointGroupController.AccessPointGroupsPOSTAsync(
                    AppSettings.ProjectId,
                    nameArg.Value,
                    makeDefaultOptions.HasValue()).Result;

                Program.PrintResult(res);
            });
        }

    }
}