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
            ApiBase.Authorization = AppSettings.Authorization;
            ApiBase.BaseAddress = AppSettings.ServerUrl;

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
            if (args.Contains("/test"))
            {
                InitTest().Wait();
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

            cmdApp.Command(nameof(CreateAccess), CreateAccess);
            cmdApp.Command(nameof(GenerateServerAuthorization), GenerateServerAuthorization);
            cmdApp.Command(nameof(GetAccessKey), GetAccessKey);
            cmdApp.Command("accessTokenGroup", ManageAccessTokenGroup);
            ServerEndPointCmd.AddCommand(cmdApp);

            try
            {
                cmdApp.Execute(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static void ManageAccessTokenGroup(CommandLineApplication cmdApp)
        {
            cmdApp.Command("create", CreateAccessTokenGroup);
        }

        private static void CreateAccessTokenGroup(CommandLineApplication cmdApp)
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

                PrintResult(res);
            });
        }

        public static void PrintResult(object obj)
        {
            JsonSerializerOptions options = new()
            {
                WriteIndented = true
            };
            Console.WriteLine(JsonSerializer.Serialize(obj, options));
        }

        private static void GenerateServerAuthorization(CommandLineApplication cmdApp)
        {
            const string defIssuer = "auth.vpnhood.com";
            const string defAudience = "access.vpnhood.com";
            const string defSubject = "VpnServer";
            const string defRole = "VpnServer";

            cmdApp.Description = "Generate a ServerKey";
            var keyOption = cmdApp.Option("-key", "Symmetric key in base64. Default: <New key will be created>",
                CommandOptionType.SingleValue);
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

        private static void CreateAccess(CommandLineApplication cmdApp)
        {
            const int defaultTraffic = 0;
            const int defaultMaxClient = 3;
            const int defaultLifetime = 0;
            cmdApp.Description = "Create an accessKey";

            var groupIdOption = cmdApp.Option("-groupId", "Default: Default groupId", CommandOptionType.SingleValue);
            var nameOption = cmdApp.Option("-name", "Default: <null>", CommandOptionType.SingleValue);
            var isPublicOption = cmdApp.Option("-public", "Default: create a private key", CommandOptionType.NoValue);
            var maxTrafficOption = cmdApp.Option("-maxTraffic", $"in MB, Default: {defaultTraffic} MB",
                CommandOptionType.SingleValue);
            var maxClientOption = cmdApp.Option("-maxClient", $"Maximum concurrent client, Default: {defaultMaxClient}",
                CommandOptionType.SingleValue);
            var lifetimeOption = cmdApp.Option("-lifetime",
                $"The count of working days after first connection, 0 means no expiration time, Default: {defaultLifetime}",
                CommandOptionType.SingleValue);
            var tokenUrlOption = cmdApp.Option("-tokenUrl", "Default: <null>", CommandOptionType.SingleValue);

            cmdApp.OnExecuteAsync(async ct =>
            {
                AccessTokenController accessTokenController = new();
                var accessToken = await accessTokenController.AccessTokensPOSTAsync(AppSettings.ProjectId,
                    new AccessTokenCreateParams
                    {
                        AccessTokenGroupId = groupIdOption.HasValue() ? Guid.Parse(groupIdOption.Value()!) : null,
                        AccessTokenName = nameOption.HasValue() ? nameOption.Value()! : null,
                        IsPublic = isPublicOption.HasValue(),
                        Lifetime = lifetimeOption.HasValue() ? int.Parse(lifetimeOption.Value()!) : defaultLifetime,
                        MaxClient = maxClientOption.HasValue() ? int.Parse(maxClientOption.Value()!) : defaultMaxClient,
                        MaxTraffic =
                            maxTrafficOption.HasValue() ? int.Parse(maxTrafficOption.Value()!) : defaultTraffic,
                        Url = tokenUrlOption.HasValue() ? tokenUrlOption.Value() : null
                    }, ct);

                var accessKey =
                    await accessTokenController.AccessKeyAsync(AppSettings.ProjectId, accessToken.AccessTokenId, ct);
                Console.WriteLine($"AccessKey\n{accessKey.Key}");
            });
        }

        private static void GetAccessKey(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Get AccessKey by tokenId";
            var accessTokenIdOption =
                cmdApp.Option("-tid|--accessTokenId", "* Required", CommandOptionType.SingleValue);

            cmdApp.OnExecute(() =>
            {
                if (!accessTokenIdOption.HasValue()) throw new ArgumentNullException(accessTokenIdOption.LongName);
                var accessTokenId = Guid.Parse(accessTokenIdOption.Value()!);

                AccessTokenController accessTokenController = new();
                var accessKey = accessTokenController.AccessKeyAsync(AppSettings.ProjectId, accessTokenId).Result;
                Console.WriteLine($"AccessKey\n{accessKey.Key}");
            });
        }

        private static async Task InitTest()
        {
            var projectId = Guid.Parse("{8D0B44B1-808A-4A38-AE45-B46AF985F280}");

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
            ServerEndPointController serverEndPointController = new();
            var serverEndPoint = IPEndPoint.Parse("192.168.86.136:9443");
            try
            {
                await serverEndPointController.ServerEndpointsGETAsync(projectId, serverEndPoint.ToString());
                Console.WriteLine($"ServerEndPoint already exists. {serverEndPoint}");
            }
            catch
            {
                await serverEndPointController.ServerEndpointsPOSTAsync(projectId, serverEndPoint.ToString(),
                    new ServerEndPointCreateParams
                    {
                        CertificateRawData = await File.ReadAllBytesAsync("foo.test.pfx"),
                        CertificatePassword = "1",
                        MakeDefault = true
                    });
                Console.WriteLine($"ServerEndPoint has been created. {serverEndPoint}");
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
                    new AccessTokenCreateParams {AccessTokenId = accessTokenId, Secret = new byte[16]});
                Console.WriteLine("Token has been created.");
            }

            var accessKey = await accessTokenController.AccessKeyAsync(projectId, accessTokenId);
            Console.WriteLine(accessKey.Key);
        }
    }
}