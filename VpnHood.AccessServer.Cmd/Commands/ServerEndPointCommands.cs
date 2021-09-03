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
            cmdApp.Command("delete", Delete);
        }

        private static void Delete(CommandLineApplication cmdApp)
        {
            var publicEndPointArg = cmdApp.Argument("publicEndPoint","").IsRequired();
            cmdApp.OnExecuteAsync(async ct =>
            {
                ServerEndPointController serverEndPointController = new();
                await serverEndPointController.ServerEndpointsDELETEAsync(AppSettings.ProjectId,
                    publicEndPointArg.Value!, ct);
                Console.WriteLine("Deleted!");
            });
        }

        private static void Create(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Create a Certificate and add it to the server.";
            var publicEndPointArg = cmdApp.Argument("publicEndPoint","").IsRequired();
            var privateEndPointOptions = cmdApp.Option("-privateEndPoint", "Private EndPoint that Public is mapped to. Default: null", CommandOptionType.SingleValue);
            var subjectNameOption = cmdApp.Option("-sn|--subjectName", "Default: random name; example: CN=site.com", CommandOptionType.SingleValue);
            var groupIdOption = cmdApp.Option("-groupId", "Default: Default groupId", CommandOptionType.SingleValue);
            var cerFileOption = cmdApp.Option("-certFile", "Path to certificate file. Default: create new using subjectName", CommandOptionType.SingleValue);
            var cerFilePasswordOption = cmdApp.Option("-certPass", "Certificate password", CommandOptionType.SingleValue);
            var makeDefaultOption = cmdApp.Option("-makeDefault", "default: not set", CommandOptionType.NoValue);

            cmdApp.OnExecuteAsync(async ct =>
            {
                ServerEndPointController serverEndPointController = new();
                await serverEndPointController.ServerEndpointsPOSTAsync(AppSettings.ProjectId,
                    publicEndPointArg.Value!,
                    new ServerEndPointCreateParams
                    {
                        SubjectName = subjectNameOption.HasValue() ? subjectNameOption.Value() : null,
                        PrivateEndPoint = privateEndPointOptions.HasValue() ? privateEndPointOptions.Value() : null,
                        CertificateRawData = cerFileOption.HasValue()
                            ? await File.ReadAllBytesAsync(cerFileOption.Value()!, ct)
                            : null,
                        CertificatePassword = cerFilePasswordOption.Value(),
                        AccessTokenGroupId = groupIdOption.HasValue() ? Guid.Parse(groupIdOption.Value()!) : null,
                        MakeDefault = makeDefaultOption.HasValue()
                    }, ct);

                Console.WriteLine("Created!");
            });
        }

        private static void Update(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Update an EndPoint";
            var publicEndPointArg = cmdApp.Argument("publicEndPoint","").IsRequired();
            var privateEndPointOptions = cmdApp.Option("-privateEndPoint","", CommandOptionType.SingleValue);
            var groupIdOption = cmdApp.Option("-groupId", "", CommandOptionType.SingleValue);
            var cerFileOption = cmdApp.Option("-certFile", "Path to certificate file.", CommandOptionType.SingleValue);
            var cerFilePasswordOption = cmdApp.Option("-certPass", "Certificate password", CommandOptionType.SingleValue);
            var makeDefaultOption = cmdApp.Option("-makeDefault", "", CommandOptionType.NoValue);

            cmdApp.OnExecuteAsync(async ct =>
            {
                ServerEndPointController serverEndPointController = new();
                await serverEndPointController.ServerEndpointsPUTAsync(AppSettings.ProjectId,
                    publicEndPointArg.Value!,
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
                            ? new GuidWise { Value = Guid.Parse(groupIdOption.Value()!) }
                            : null,
                        PrivateEndPoint = privateEndPointOptions.HasValue()
                            ? new StringWise { Value = privateEndPointOptions.Value()! }
                            : null,
                    }, ct);

                Console.WriteLine("Updated!");
            });
        }
    }
}