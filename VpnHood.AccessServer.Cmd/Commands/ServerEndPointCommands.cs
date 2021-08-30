using System;
using System.IO;
using McMaster.Extensions.CommandLineUtils;
using VpnHood.AccessServer.Cmd.Apis;

namespace VpnHood.AccessServer.Cmd.Commands
{
    internal static class ServerEndPointCommands
    {
        private static AppSettings AppSettings => Program.AppSettings;

        public static void AddCommands(CommandLineApplication cmdApp)
        {
            cmdApp.Command("serverEndPoints", MainCommand);
        }

        private static void MainCommand(CommandLineApplication cmdApp)
        {
            cmdApp.Command("create", Create);
            cmdApp.Command("update", Update);
        }

        private static void Create(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Create a Certificate and add it to the server.";
            var publicEndPointOption =
                cmdApp.Option("-ep|--publicEndPoint", "* Required", CommandOptionType.SingleValue).IsRequired();
            var subjectNameOption = cmdApp.Option("-sn|--subjectName", "Default: random name; example: CN=site.com",
                CommandOptionType.SingleValue);
            var groupIdOption = cmdApp.Option("-groupId", "Default: Default groupId", CommandOptionType.SingleValue);
            var cerFileOption = cmdApp.Option("-certFile",
                "Path to certificate file. Default: create new using subjectName", CommandOptionType.SingleValue);
            var cerFilePasswordOption =
                cmdApp.Option("-certPass", "Certificate password", CommandOptionType.SingleValue);
            var makeDefaultOption = cmdApp.Option("-makeDefault", "default: not set", CommandOptionType.NoValue);

            cmdApp.OnExecuteAsync(async ct =>
            {
                ServerEndPointController serverEndPointController = new();
                await serverEndPointController.ServerEndpointsPOSTAsync(AppSettings.ProjectId,
                    publicEndPointOption.Value()!,
                    new ServerEndPointCreateParams
                    {
                        SubjectName = subjectNameOption.HasValue() ? subjectNameOption.Value() : null,
                        CertificateRawData = cerFileOption.HasValue()
                            ? await File.ReadAllBytesAsync(cerFileOption.Value()!, ct)
                            : null,
                        CertificatePassword = cerFilePasswordOption.Value(),
                        AccessTokenGroupId = groupIdOption.HasValue() ? Guid.Parse(groupIdOption.Value()!) : null,
                        MakeDefault = makeDefaultOption.HasValue()
                    }, ct);

                Console.WriteLine("Done!");
            });
        }

        private static void Update(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Update an EndPoint";
            var publicEndPointOption =
                cmdApp.Option("-ep|--publicEndPoint", "* Required", CommandOptionType.SingleValue).IsRequired();
            var groupIdOption = cmdApp.Option("-groupId", "Default: Default groupId", CommandOptionType.SingleValue);
            var cerFileOption = cmdApp.Option("-certFile",
                "Path to certificate file. Default: create new using subjectName", CommandOptionType.SingleValue);
            var cerFilePasswordOption =
                cmdApp.Option("-certPass", "Certificate password", CommandOptionType.SingleValue);
            var makeDefaultOption = cmdApp.Option("-makeDefault", "default: not set", CommandOptionType.NoValue);

            cmdApp.OnExecuteAsync(async ct =>
            {
                ServerEndPointController serverEndPointController = new();
                await serverEndPointController.ServerEndpointsPUTAsync(AppSettings.ProjectId,
                    publicEndPointOption.Value()!,
                    new ServerEndPointUpdateParams
                    {
                        CertificateRawData = cerFileOption.HasValue()
                            ? new ByteArrayWise {Value = await File.ReadAllBytesAsync(cerFileOption.Value()!, ct)}
                            : null,
                        CertificatePassword = cerFilePasswordOption.HasValue()
                            ? new StringWise {Value = cerFilePasswordOption.Value()!}
                            : null,
                        MakeDefault = makeDefaultOption.HasValue()
                            ? new BooleanWise {Value = makeDefaultOption.HasValue()}
                            : null,
                        AccessTokenGroupId = groupIdOption.HasValue()
                            ? new GuidWise {Value = Guid.Parse(groupIdOption.Value()!)}
                            : null
                    }, ct);

                Console.WriteLine("Done!");
            });
        }
    }
}