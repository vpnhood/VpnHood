using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using VpnHood.AccessServer.Apis;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace VpnHood.AccessServer.Cmd
{
    internal class Program
    {
        private static readonly HttpClient _httpClient = new();

        public static AppSettings AppSettings { get; private set; }


        private static void Main(string[] args)
        {
            // find settings file
            var appSettingsFilePath =
                Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"); // check same directory

            // load AppSettings
            if (File.Exists(appSettingsFilePath))
                AppSettings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(appSettingsFilePath));
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
            cmdApp.VersionOption("-n|--version", typeof(Program).Assembly.GetName().Version.ToString());

            cmdApp.Command(nameof(CreatePublicAccessKey), CreatePublicAccessKey);
            cmdApp.Command(nameof(CreatePrivateAccessKey), CreatePrivateAccessKey);
            cmdApp.Command(nameof(CreateCertificate), CreateCertificate);
            cmdApp.Command(nameof(ImportCertificate), ImportCertificate);
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
                AccessTokenGroupClient accessTokenGroupClient = new();
                var res = accessTokenGroupClient.AccessTokenGroupsPOSTAsync(
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

        private static object SendRequest(string api, HttpMethod httpMethod, object queryParams = null,
            object bodyParams = null)
        {
            var uriBuilder = new UriBuilder(new Uri(AppSettings.ServerUrl, api));
            var query = HttpUtility.ParseQueryString(string.Empty);

            // use query string
            if (queryParams != null)
                foreach (var prop in queryParams.GetType().GetProperties())
                {
                    var value = prop.GetValue(queryParams, null)?.ToString();
                    if (value != null)
                        query.Add(prop.Name, value);
                }

            uriBuilder.Query = query.ToString();

            // create request
            uriBuilder.Query = query.ToString();
            var requestMessage = new HttpRequestMessage(httpMethod, uriBuilder.Uri);
            requestMessage.Headers.Add("authorization", AppSettings.Authorization);
            if (bodyParams != null)
                requestMessage.Content = new StringContent(JsonSerializer.Serialize(bodyParams), Encoding.UTF8,
                    "application/json");

            // send request
            var res = _httpClient.Send(requestMessage);
            using var stream = res.Content.ReadAsStream();
            var streamReader = new StreamReader(stream);
            var ret = streamReader.ReadToEnd();

            if (res.StatusCode != HttpStatusCode.OK)
                throw new Exception(
                    $"Invalid status code from RestAccessServer! Status: {res.StatusCode}, Message: {ret}");

            if (res.Content.Headers.ContentType.MediaType == "text/plain")
                return ret;

            return JsonConvert.DeserializeObject(ret);
        }

        private static string SendRequest(string api, object paramerters, HttpMethod httpMethod, object content = null)
        {
            if (paramerters == null) paramerters = new { };
            var uriBuilder = new UriBuilder(AppSettings.ServerUrl);
            var query = HttpUtility.ParseQueryString(string.Empty);
            uriBuilder.Path = api;

            // use query string
            var type = paramerters.GetType();
            foreach (var prop in type.GetProperties())
            {
                var value = prop.GetValue(paramerters, null)?.ToString();
                if (value != null)
                    query.Add(prop.Name, value);
            }

            uriBuilder.Query = query.ToString();

            var uri = uriBuilder.ToString();

            var requestMessage = new HttpRequestMessage(httpMethod, uri);
            requestMessage.Headers.Add("authorization", AppSettings.Authorization);
            if (content is string)
                requestMessage.Content = new StringContent(content as string, Encoding.UTF8, "application/json");
            else if (content is byte[]) requestMessage.Content = new ByteArrayContent(content as byte[]);

            // send request
            var res = _httpClient.Send(requestMessage);
            using var stream = res.Content.ReadAsStream();
            var streamReader = new StreamReader(stream);
            var ret = streamReader.ReadToEnd();

            if (res.StatusCode != HttpStatusCode.OK)
                throw new Exception(ret);
            return ret;
        }

        private static void GenerateServerAuthorization(CommandLineApplication cmdApp)
        {
            var defIssuer = "auth.vpnhood.com";
            var defAudience = "access.vpnhood.com";
            var defSubject = "VpnServer";
            var defRole = "VpnServer";

            cmdApp.Description = "Generate a ServerKey";
            var keyOption = cmdApp.Option("-key", "Symmetric key in base64. Default: <New key will be created>",
                CommandOptionType.SingleValue);
            var issuerOption = cmdApp.Option("-issuer", $"Default: {defIssuer}", CommandOptionType.SingleValue);
            var audienceOption = cmdApp.Option("-audience", $"Default: {defAudience}", CommandOptionType.SingleValue);
            var subjectOption = cmdApp.Option("-subject", $"Default: {defSubject}", CommandOptionType.SingleValue);
            var roleOption = cmdApp.Option("-role", $"Default: {defRole}", CommandOptionType.SingleValue);
            var projectIdOption = cmdApp.Option("-projectId", null, CommandOptionType.SingleValue);

            cmdApp.OnExecute(() =>
            {
                var aes = Aes.Create();
                aes.KeySize = 128;
                var encKey = aes.Key;

                // set key
                if (keyOption.HasValue())
                {
                    aes.Key = Convert.FromBase64String(keyOption.Value());
                }
                else
                {
                    aes.GenerateKey();
                    Console.BackgroundColor = ConsoleColor.Blue;
                    Console.WriteLine("[NewKey]");
                    Console.ResetColor();
                    Console.WriteLine($"{Convert.ToBase64String(aes.Key)}\n\n");
                }

                // create claims
                var claims = new List<Claim>
                {
                    new("roles", roleOption.HasValue() ? roleOption.Value() : defRole)
                };
                if (projectIdOption.HasValue())
                    claims.Add(new Claim("project_id", projectIdOption.Value()));

                // create jwt
                var jwt = JwtTool.CreateSymJwt(aes,
                    issuerOption.HasValue() ? issuerOption.Value() : defIssuer,
                    audienceOption.HasValue() ? audienceOption.Value() : defAudience,
                    subjectOption.HasValue() ? subjectOption.Value() : defSubject,
                    claims.ToArray());

                Console.BackgroundColor = ConsoleColor.Blue;
                Console.WriteLine("[ServerJwt]");
                Console.ResetColor();
                Console.WriteLine("Bearer " + jwt);
            });
        }

        private static void CreateCertificate(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Create a Certificate and add it to the server.";
            var serverEndPointOption =
                cmdApp.Option("-ep|--serverEndPoint", "* Required", CommandOptionType.SingleValue).IsRequired();
            var subjectNameOption = cmdApp.Option("-sn|--subjectName", "Default: random name; example: CN=site.com",
                CommandOptionType.SingleValue);

            cmdApp.OnExecute(() =>
            {
                var parameters = new
                {
                    serverEndPoint = serverEndPointOption.Value(),
                    subjectName = subjectNameOption.HasValue() ? subjectNameOption.Value() : null
                };
                SendRequest("Certificate/Create", parameters, HttpMethod.Post);
                Console.WriteLine($"Certificate has been created and assigned to {parameters.serverEndPoint}");
            });
        }

        private static void ImportCertificate(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Import a Certificate it to the server";
            var serverEndPointOption =
                cmdApp.Option("-ep|--serverEndPoint", "* Required", CommandOptionType.SingleValue).IsRequired();
            var certFileOption = cmdApp.Option("-cf|--certFile", "* Required, Certificate file path in PFX format",
                CommandOptionType.SingleValue).IsRequired();
            var passwordOption = cmdApp.Option("-p|--password", "Default: <null>", CommandOptionType.SingleValue);
            var overwriteOption = cmdApp.Option("-o|--overwrite", null, CommandOptionType.NoValue);

            cmdApp.OnExecute(() =>
            {
                var parameters = new
                {
                    serverEndPoint = serverEndPointOption.Value(),
                    overwrite = overwriteOption.HasValue()
                };

                var certFile = certFileOption.Value();
                var password = passwordOption.HasValue() ? passwordOption.Value() : null;
                var x509Certificate = new X509Certificate2(certFile, password);
                var rawData = x509Certificate.Export(X509ContentType.Pfx);

                SendRequest("Certificate/Import", parameters, HttpMethod.Post, JsonSerializer.Serialize(rawData));
                Console.WriteLine($"Certificate has been created and assigned to {parameters.serverEndPoint}");
            });
        }

        private static void CreatePrivateAccessKey(CommandLineApplication cmdApp)
        {
            var defaultTraffic = 0;
            var defaultMaxClient = 3;
            var defaultLifetime = 90;
            cmdApp.Description = "Create a private accessKey and add it to the server";

            var serverEndPointOption =
                cmdApp.Option("-ep|--serverEndPoint", "* Required", CommandOptionType.SingleValue).IsRequired();
            var nameOption = cmdApp.Option("-name", "Default: <null>", CommandOptionType.SingleValue);
            var maxTrafficOption = cmdApp.Option("-maxTraffic", $"in MB, Default: {defaultTraffic} MB",
                CommandOptionType.SingleValue);
            var maxClientOption = cmdApp.Option("-maxClient",
                $"Maxiumum concurrent client, Default: {defaultMaxClient}", CommandOptionType.SingleValue);
            var lifetimeOption = cmdApp.Option("-lifetime",
                $"The count of working days after first connection, Default: {defaultLifetime}",
                CommandOptionType.SingleValue);
            var tokenUrlOption = cmdApp.Option("-tokenUrl", "Default: <null>", CommandOptionType.SingleValue);

            cmdApp.OnExecute(() =>
            {
                //check serverEndPointOption
                if (!serverEndPointOption.HasValue()) throw new ArgumentNullException(serverEndPointOption.LongName);
                if (IPEndPoint.Parse(serverEndPointOption.Value()).Port == 0)
                    throw new ArgumentException("Invalid Port! use x.x.x.x:443", serverEndPointOption.ValueName);
                if (!serverEndPointOption.HasValue()) throw new ArgumentNullException(serverEndPointOption.LongName);

                var parameters = new
                {
                    serverEndPoint = serverEndPointOption.Value(),
                    tokenName = nameOption.HasValue() ? nameOption.Value() : null,
                    maxTraffic = maxTrafficOption.HasValue()
                        ? (long.Parse(maxTrafficOption.Value()) * 1000000).ToString()
                        : (defaultTraffic * 1000000).ToString(),
                    maxClient = maxClientOption.HasValue() ? long.Parse(maxClientOption.Value()) : defaultMaxClient,
                    lifetime = lifetimeOption.HasValue() ? long.Parse(lifetimeOption.Value()) : defaultLifetime,
                    tokenUrl = tokenUrlOption.HasValue() ? tokenUrlOption.Value() : null
                };

                //var accessTokenStr = SendRequest($"access-tokens/CreatePrivate", parameters, HttpMethod.Post);
                ///dynamic accessToken = JsonSerializer.Serialize(accessTokenStr);

                //var accessKey = SendRequest($"AccessToken/GetAccessKey", new { accessToken.accessTokenId }, HttpMethod.Get);
                //Console.WriteLine($"AccessKey\n{accessKey}");
                throw new NotImplementedException();
            });
        }

        private static void CreatePublicAccessKey(CommandLineApplication cmdApp)
        {
            var defaultTraffic = 1000;
            cmdApp.Description = "Create a public accessKey and add it to the server";

            var serverEndPointOption =
                cmdApp.Option("-ep|--serverEndPoint", "* Required", CommandOptionType.SingleValue).IsRequired();
            var nameOption = cmdApp.Option("-name", "Default: <null>", CommandOptionType.SingleValue);
            var maxTrafficOption = cmdApp.Option("-maxTraffic", $"in MB, Default: {defaultTraffic} MB",
                CommandOptionType.SingleValue);
            var tokenUrlOption = cmdApp.Option("-tokenUrl", "Default: <null>", CommandOptionType.SingleValue);

            cmdApp.OnExecute(() =>
            {
                //check serverEndPointOption
                if (!serverEndPointOption.HasValue()) throw new ArgumentNullException(serverEndPointOption.LongName);
                if (IPEndPoint.Parse(serverEndPointOption.Value()).Port == 0)
                    throw new ArgumentException("Invalid Port! use x.x.x.x:443", serverEndPointOption.ValueName);
                if (!serverEndPointOption.HasValue()) throw new ArgumentNullException(serverEndPointOption.LongName);

                var parameters = new
                {
                    serverEndPoint = serverEndPointOption.Value(),
                    tokenName = nameOption.HasValue() ? nameOption.Value() : null,
                    maxTraffic = maxTrafficOption.HasValue()
                        ? (long.Parse(maxTrafficOption.Value()) * 1000000).ToString()
                        : (defaultTraffic * 1000000).ToString(),
                    tokenUrl = tokenUrlOption.HasValue() ? tokenUrlOption.Value() : null
                };

                var accessTokenStr = SendRequest("AccessToken/CreatePublic", parameters, HttpMethod.Post);
                dynamic accessToken = JsonConvert.DeserializeObject(accessTokenStr);

                var accessKey = SendRequest("AccessToken/GetAccessKey", new {accessToken.accessTokenId},
                    HttpMethod.Get);
                Console.WriteLine($"AccessKey\n{accessKey}");
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
                var accessTokenId = Guid.Parse(accessTokenIdOption.Value());

                AccessTokenClient accessTokenClient = new();
                var accessKey = accessTokenClient.AccessKeyAsync(AppSettings.ProjectId, accessTokenId).Result;
                Console.WriteLine($"AccessKey\n{accessKey.Key}");
            });
        }

        private static async Task InitTest()
        {
            var projectId = Guid.Parse("{8D0B44B1-808A-4A38-AE45-B46AF985F280}");

            // create project if not exists
            ProjectClient projectClient = new();
            try
            {
                await projectClient.ProjectsGETAsync(projectId);
                Console.WriteLine("Project already exists.");
            }
            catch
            {
                await projectClient.ProjectsPOSTAsync(projectId);
                Console.WriteLine("Project has been created.");
            }

            // create certificate for default group
            ServerEndPointClient serverEndPointClient = new();
            var serverEndPoint = IPEndPoint.Parse("192.168.86.136:9443");
            try
            {
                await serverEndPointClient.ServerEndpointsGETAsync(projectId, serverEndPoint.ToString());
                Console.WriteLine($"ServerEndPoint already exists. {serverEndPoint}");
            }
            catch
            {
                await serverEndPointClient.ServerEndpointsPOSTAsync(projectId, serverEndPoint.ToString(),
                    new ServerEndPointCreateParams
                    {
                        CertificateRawData = File.ReadAllBytes("foo.test.pfx"),
                        CertificatePassword = "1",
                        MakeDefault = true
                    });
                Console.WriteLine($"ServerEndPoint has been created. {serverEndPoint}");
            }

            AccessTokenClient accessTokenClient = new();
            var accessTokenId = Guid.Parse("{BE0160D6-4D56-4BEE-9B3F-10F4D655C49C}");
            try
            {
                await accessTokenClient.AccessTokensGETAsync(projectId, accessTokenId);
                Console.WriteLine("Token already exists.");
            }
            catch
            {
                await accessTokenClient.AccessTokensPOSTAsync(projectId,
                    new AccessTokenCreateParams {AccessTokenId = accessTokenId, Secret = new byte[16]});
                Console.WriteLine("Token has been created.");
            }

            var accessKey = await accessTokenClient.AccessKeyAsync(projectId, accessTokenId);
            Console.WriteLine(accessKey.Key);
        }
    }
}