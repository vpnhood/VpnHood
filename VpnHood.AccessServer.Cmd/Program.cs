using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using VpnHood.AccessServer.Cmd.Apis;
using VpnHood.AccessServer.Cmd.Commands;

namespace VpnHood.AccessServer.Cmd
{
    internal class Program
    {
        public static AppSettings AppSettings { get; private set; } = null!;

        private static void Main(string[] args)
        {
            // find settings file
            var appSettingsFilePath =
                Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"); // check same directory

            // load AppSettings
            if (File.Exists(appSettingsFilePath))
                AppSettings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(appSettingsFilePath)) ??
                              throw new Exception("Could not load appsettings");
            ApiBase.Authorization = AppSettings.Authorization ?? throw new InvalidOperationException($"{nameof(AppSettings.Authorization)} is not set!");
            ApiBase.BaseAddress = AppSettings.ServerUrl ?? throw new InvalidOperationException($"{nameof(AppSettings.ServerUrl)} is not set!");
            if (AppSettings.ProjectId == Guid.Empty) throw new InvalidOperationException($"{nameof(AppSettings.ProjectId)} is not set!");

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine();
            Console.WriteLine($"ServerUrl: {AppSettings.ServerUrl}");
            Console.ResetColor();

            // replace "/?"
            for (var i = 0; i < args.Length; i++)
                if (args[i] == "/?")
                    args[i] = "-?";

            // set default
            if (args.Length == 0) args = new[] {"-?"};

            // run test
            if (args.Contains("/seed"))
            {
                SeedDb().Wait();
                return;
            }

            var cmdApp = new CommandLineApplication
            {
                AllowArgumentSeparator = true,
                Name = typeof(Program).Assembly.GetName().Name,
                FullName = "VpnHood AccessServer",
                MakeSuggestionsInErrorMessage = true
            };

            cmdApp.HelpOption(true);
            cmdApp.VersionOption("-n|--version", typeof(Program).Assembly.GetName().Version?.ToString());

            cmdApp.Command(nameof(GenerateServerAuthorization), GenerateServerAuthorization);
            AccessTokenCommands.AddCommands(cmdApp);
            AccessPointGroupCommands.AddCommands(cmdApp);
            AccessPointCommands.AddCommands(cmdApp);
            CertificateCommands.AddCommands(cmdApp);

            try
            {
                cmdApp.Execute(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public static string FormatResult(object obj)
        {
            return JsonSerializer.Serialize(obj, new JsonSerializerOptions{WriteIndented = true, });
        }

        public static void PrintResult(object obj)
        {
            Console.WriteLine(FormatResult(obj));
        }

        private static void GenerateServerAuthorization(CommandLineApplication cmdApp)
        {
            const string defIssuer = "auth.vpnhood.com";
            const string defAudience = "access.vpnhood.com";
            const string defSubject = "VpnServer";
            const string defRole = "VpnServer";

            var keyOption = cmdApp.Option("-key", "Symmetric key in base64. Default: <New key will be created>",CommandOptionType.SingleValue);
            var issuerOption = cmdApp.Option("-issuer", $"Default: {defIssuer}", CommandOptionType.SingleValue);
            var audienceOption = cmdApp.Option("-audience", $"Default: {defAudience}", CommandOptionType.SingleValue);
            var subjectOption = cmdApp.Option("-subject", $"Default: {defSubject}", CommandOptionType.SingleValue);
            var roleOption = cmdApp.Option("-role", $"Default: {defRole}", CommandOptionType.SingleValue);
            var projectIdOption = cmdApp.Option("-projectId", "", CommandOptionType.SingleValue);

            cmdApp.OnExecute(() =>
            {
                var aes = Aes.Create();
                aes.KeySize = 128;
                aes.GenerateKey();
                var encKey = aes.Key;

                // set key
                if (keyOption.HasValue())
                {
                    encKey = Convert.FromBase64String(keyOption.Value()!);
                }
                else
                {
                    Console.BackgroundColor = ConsoleColor.Blue;
                    Console.WriteLine("[NewKey]");
                    Console.ResetColor();
                    Console.WriteLine($"{Convert.ToBase64String(encKey)}\n\n");
                }

                // create claims
                var claims = new List<Claim>
                {
                    new("roles", roleOption.HasValue() ? roleOption.Value()! : defRole)
                };
                if (projectIdOption.HasValue())
                    claims.Add(new Claim("project_id", projectIdOption.Value()!));

                // create jwt
                var jwt = JwtTool.CreateSymJwt(encKey,
                    issuerOption.HasValue() ? issuerOption.Value()! : defIssuer,
                    audienceOption.HasValue() ? audienceOption.Value()! : defAudience,
                    subjectOption.HasValue() ? subjectOption.Value()! : defSubject,
                    claims.ToArray());

                Console.BackgroundColor = ConsoleColor.Blue;
                Console.WriteLine("[ServerJwt]");
                Console.ResetColor();
                Console.WriteLine("Bearer " + jwt);
            });
        }

        private static async Task SeedDb()
        {
            var projectId = AppSettings.ProjectId;

            // create project if not exists
            ProjectController projectController = new();
            try
            {
                await projectController.ProjectsGETAsync(projectId);
                Console.WriteLine("Project already exists.");
            }
            catch
            {
                await projectController.ProjectsPOSTAsync(projectId);
                Console.WriteLine("Project has been created.");
            }

            // create certificate for default group
            AccessPointController accessPointController = new();
            var publicEndPoint = IPEndPoint.Parse("192.168.86.136:9443");
            try
            {
                await accessPointController.AccessPointsGETAsync(projectId, publicEndPoint.ToString());
                Console.WriteLine($"AccessPoint already exists. {publicEndPoint}");
            }
            catch
            {
                await accessPointController.AccessPointsPOSTAsync(projectId,
                    new AccessPointCreateParams
                    {
                        PublicEndPoint = publicEndPoint.ToString(),
                        MakeDefault = true
                    });
                Console.WriteLine($"AccessPoint has been created. {publicEndPoint}");
            }

            AccessTokenController accessTokenController = new();
            var accessTokenId = Guid.Parse("{BE0160D6-4D56-4BEE-9B3F-10F4D655C49C}");
            try
            {
                await accessTokenController.AccessTokensGETAsync(projectId, accessTokenId);
                Console.WriteLine("Token already exists.");
            }
            catch
            {
                await accessTokenController.AccessTokensPOSTAsync(projectId,
                    new AccessTokenCreateParams {AccessTokenId = accessTokenId, Secret = Convert.FromHexString("0x8DF9E7A28E6371A904B05803C8A94FEF")});
                Console.WriteLine("Token has been created.");
            }

            var accessKey = await accessTokenController.AccessKeyAsync(projectId, accessTokenId);
            Console.WriteLine(accessKey.Key);
        }
    }
}