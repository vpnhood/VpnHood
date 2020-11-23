using McMaster.Extensions.CommandLineUtils;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace VpnHood.AccessServer.Cmd
{
    class Program
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public static string AppFolderPath { get; private set; }
        public static AppSettings AppSettings { get; private set; }

        static void Main(string[] args)
        {
            AppFolderPath = Path.GetDirectoryName(typeof(Program).Assembly.Location);

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

            cmdApp.Command("newPublicAccessKey", GeneratePublicAccessKey);
            cmdApp.Command("newServerToken", GenerateServerBearerToken);
            cmdApp.Command("CreateSslCertificate", CreateCertificate);

            try
            {
                cmdApp.Execute(args);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static string SendRequest(string api, object paramerters)
        {
            var uriBuilder = new UriBuilder(AppSettings.ServerUrl);
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            uriBuilder.Path = api;
            
            var type = paramerters.GetType();
            foreach (var prop in type.GetProperties())
            {
                var value = prop.GetValue(paramerters, null)?.ToString();
                if (value!=null)
                    query.Add(prop.Name, value);
            }

            uriBuilder.Query = query.ToString();
            var uri = uriBuilder.ToString();

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, uri);
            requestMessage.Headers.Add("authorization", "Bearer " + AppSettings.ServerBearerToken);
            
            // send request
            var res = _httpClient.Send(requestMessage);
            using var stream = res.Content.ReadAsStream();
            var streamReader = new StreamReader(stream);
            var ret = streamReader.ReadToEnd();

            if (res.StatusCode != HttpStatusCode.OK)
                throw new Exception(ret);
            return ret;
        }

        private static void GenerateServerBearerToken(CommandLineApplication cmdApp)
        {
            var defIssuer = "auth.vpnhood.com";
            var defAudience = "access.vpnhood.com";
            var defSubject = "VpnServer";
            var defRole = "VpnServer";

            cmdApp.Description = "Generate a ServerKey";
            var keyOption = cmdApp.Option("-key", $"Symmetric key in base64. Default: <New key will be created>", CommandOptionType.SingleValue);
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

        private static void CreateCertificate(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Create a Certificate";
            var serverEndPointOption = cmdApp.Option("-ep|--serverEndPoint", "* Required", CommandOptionType.SingleValue);
            var subjectNameOption = cmdApp.Option("-subjectName", "Default: random name", CommandOptionType.SingleValue);

            cmdApp.OnExecute(() =>
            {
                var parameters = new
                {
                    serverEndPoint = serverEndPointOption.Value(),
                    subjectName = subjectNameOption.HasValue() ? subjectNameOption.Value() : null
                };
                SendRequest($"Certificate/Create", parameters);
                Console.WriteLine($"Certificate has been created and assigned to {parameters.serverEndPoint}");
            });
        }

        private static void GeneratePublicAccessKey(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Generate a public accessKey";
            var serverEndPointOption = cmdApp.Option("-ep|--serverEndPoint", "* Required", CommandOptionType.SingleValue);
            var nameOption = cmdApp.Option("-name", $"Default: <null>", CommandOptionType.SingleValue);
            var maxTrafficOption = cmdApp.Option("-maxTraffic", $"in MB, Default: 500 MB", CommandOptionType.SingleValue);

            cmdApp.OnExecute(() =>
                {
                    //check serverEndPointOption
                    if (!serverEndPointOption.HasValue()) throw new ArgumentNullException(serverEndPointOption.ValueName);
                    if (IPEndPoint.Parse(serverEndPointOption.Value()).Port == 0) throw new ArgumentException("Invalid Port! use x.x.x.x:443", serverEndPointOption.ValueName);
                    if (!serverEndPointOption.HasValue()) throw new ArgumentNullException(serverEndPointOption.ValueName);

                    var parameters = new
                    {
                        serverEndPoint = serverEndPointOption.Value(),
                        tokenName = nameOption.HasValue() ? nameOption.Value() : null,
                        maxTraffic = maxTrafficOption.HasValue() ? (long.Parse(maxTrafficOption.Value()) * 1000000).ToString() : (500 * 1000000).ToString()

                    };
                    var str = SendRequest($"AccessToken/CreatePublic", parameters);
                    Console.WriteLine($"AccessKey\n{str}");

                });
        }
    }
}
