using McMaster.Extensions.CommandLineUtils;
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace VpnHood.AccessServer.Cmd
{
    class Program
    {
        public static string AppFolderPath { get; private set; }
        public static AppSettings AppSettings { get; private set; }

        static void Main(string[] args)
        {
            AppFolderPath = typeof(Program).Assembly.Location;

            // load AppSettings
            var appSettingsFilePath = Path.Combine(AppFolderPath, "appsettings.json");
            if (File.Exists(appSettingsFilePath))
                AppSettings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(appSettingsFilePath));

            // replace "/?"
            for (var i = 0; i < args.Length; i++)
                if (args[i] == "/?") args[i] = "-?";

            // set default
            if (args.Length == 0) args = new string[] { "-?" };

            var cmdApp = new CommandLineApplication()
            {
                AllowArgumentSeparator = true,
                Name = typeof(Program).Assembly.GetName().Name,
                FullName = "VpnHood AccessServer",
                MakeSuggestionsInErrorMessage = true,
            };

            cmdApp.HelpOption(true);
            cmdApp.VersionOption("-n|--version", typeof(Program).Assembly.GetName().Version.ToString());

            cmdApp.Command("accessKey", GenerateAccessKey);
            cmdApp.Command("serverJwt", GenerateServerJwt);

            try
            {
                cmdApp.Execute(args);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static void GenerateServerJwt(CommandLineApplication cmdApp)
        {
            var defIssuer = "auth.vpnhood.com";
            var defAudience = "access.vpnhood.com";
            var defSubject = "VpnServer";
            var defRole = "VpnServer";

            cmdApp.Description = "Generate a ServerKey";
            var keyOption = cmdApp.Option("-key", $"key in base64. Default: <New key will be created>", CommandOptionType.SingleValue);
            var issuerOption = cmdApp.Option("-issuer", $"Default: {defIssuer}", CommandOptionType.SingleValue);
            var audienceOption = cmdApp.Option("-audience", $"Default: {defAudience}", CommandOptionType.SingleValue);
            var subjectOption = cmdApp.Option("-subject", $"Default: {defSubject}", CommandOptionType.SingleValue);
            var roleOption = cmdApp.Option("-role", $"Default: {defRole}", CommandOptionType.SingleValue);

            cmdApp.OnExecute(() =>
            {
                var aes = Aes.Create();
                aes.KeySize = 256;
                var encKey = aes.Key;

                // set key
                if (keyOption.HasValue())
                    aes.Key = Convert.FromBase64String(keyOption.Value());
                else
                {
                    aes.GenerateKey();
                    Console.BackgroundColor = ConsoleColor.Blue;
                    Console.WriteLine($"[NewKey]");
                    Console.ResetColor();
                    Console.WriteLine($"{Convert.ToBase64String(aes.Key)}\n\n");
                }

                var jwt = JwtTool.CreateSymJwt(aes: aes,
                    issuer: issuerOption.HasValue() ? issuerOption.Value() : defIssuer,
                    audience: audienceOption.HasValue() ? audienceOption.Value() : defAudience,
                    subject: subjectOption.HasValue() ? subjectOption.Value() : defSubject,
                    role: roleOption.HasValue() ? roleOption.Value() : defRole);

                Console.BackgroundColor = ConsoleColor.Blue;
                Console.WriteLine("[ServerJwt]");
                Console.ResetColor();
                Console.WriteLine(jwt);
            });
        }

        private static void GenerateAccessKey(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Generate a token";

        }
    }
}
