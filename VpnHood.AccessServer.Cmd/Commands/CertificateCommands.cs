using System;
using System.IO;
using McMaster.Extensions.CommandLineUtils;
using VpnHood.AccessServer.Cmd.Apis;

namespace VpnHood.AccessServer.Cmd.Commands
{
    internal static class CertificateCommands
    {
        private static AppSettings AppSettings => Program.AppSettings;
        public static void AddCommands(CommandLineApplication cmdApp)
        {
            cmdApp.Command("certificates", MainCommand);
        }

        private static void MainCommand(CommandLineApplication cmdApp)
        {
            cmdApp.Command("create", Create);
            cmdApp.Command("update", Update);
            cmdApp.Command("delete", Delete);
            cmdApp.Command("list", List);
        }

        private static void List(CommandLineApplication cmdApp)
        {
            cmdApp.OnExecuteAsync(async ct =>
            {
                CertificateController certificateController = new();
                var res = await certificateController.CertificatesAllAsync(AppSettings.ProjectId, cancellationToken: ct);
                Program.PrintResult(res);
            });
        }

        private static void Create(CommandLineApplication cmdApp)
        {
            var subjectNameOption = cmdApp.Option("-sn|--subjectName", "Default: random name; example: CN=site.com", CommandOptionType.SingleValue);
            var cerFileOption = cmdApp.Option("-certFile", "Path to certificate file. Default: create new using subjectName", CommandOptionType.SingleValue);
            var cerFilePasswordOption = cmdApp.Option("-certPass", "Certificate password", CommandOptionType.SingleValue);

            cmdApp.OnExecuteAsync(async ct =>
            {
                CertificateController certificateController = new();
                var ret = await certificateController.CertificatesPOSTAsync(AppSettings.ProjectId,
                    new CertificateCreateParams
                    {
                        SubjectName = subjectNameOption.HasValue() ? subjectNameOption.Value() : null,
                        RawData = cerFileOption.HasValue()
                            ? await File.ReadAllBytesAsync(cerFileOption.Value()!, ct)
                            : null,
                        Password = cerFilePasswordOption.HasValue()
                            ? cerFilePasswordOption.Value()
                            : null
                    }, ct);

                ret.RawData = null;
                Program.PrintResult(ret);
            });

        }

        private static void Update(CommandLineApplication cmdApp)
        {
            var certificateIdArg = cmdApp.Argument("certificateId", "").IsRequired();
            var cerFileOption = cmdApp.Option("-certFile", "Path to certificate file. Default: create new using subjectName", CommandOptionType.SingleValue);
            var cerFilePasswordOption = cmdApp.Option("-certPass", "Certificate password", CommandOptionType.SingleValue);

            CertificateController certificateController = new();
            cmdApp.OnExecuteAsync(async ct =>
            {
                var ret = await certificateController.CertificatesPATCHAsync(AppSettings.ProjectId,
                    Guid.Parse(certificateIdArg.Value!),
                    new CertificateUpdateParams
                    {
                        RawData = cerFileOption.HasValue()
                            ? new ByteArrayWise { Value = await File.ReadAllBytesAsync(cerFileOption.Value()!, ct) }
                            : null,
                        Password = cerFilePasswordOption.HasValue()
                            ? new StringWise { Value = cerFilePasswordOption.Value()! }
                            : null
                    }, ct);
                Program.PrintResult(ret);
            });
        }

        private static void Delete(CommandLineApplication cmdApp)
        {
            var certificateIdArg = cmdApp.Argument("certificateId", "").IsRequired();
            cmdApp.OnExecuteAsync(async ct =>
            {
                CertificateController certificateController = new();
                await certificateController.CertificatesDELETEAsync(AppSettings.ProjectId,
                    Guid.Parse(certificateIdArg.Value!), ct);
                Console.WriteLine("Deleted!");
            });
        }

    }
}