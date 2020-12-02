﻿using McMaster.Extensions.CommandLineUtils;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace VpnHood.AccessServer.Cmd
{
    class Program
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public static AppSettings AppSettings { get; private set; }

        static void Main(string[] args)
        {
            // find settings file
            var appSettingsFilePath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"); // check same directory

            // load AppSettings
            if (File.Exists(appSettingsFilePath))
                AppSettings = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(appSettingsFilePath));

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine();
            Console.WriteLine($"ServerUrl: {AppSettings.ServerUrl}");
            Console.ResetColor();

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

            cmdApp.Command(nameof(CreatePublicAccessKey), CreatePublicAccessKey);
            cmdApp.Command(nameof(CreateCertificate), CreateCertificate);
            cmdApp.Command(nameof(GenerateServerAuthHeader), GenerateServerAuthHeader);

            try
            {
                cmdApp.Execute(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static string SendRequest(string api, object paramerters, HttpMethod httpMethod, bool useBody)
        {
            var uriBuilder = new UriBuilder(AppSettings.ServerUrl);
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            uriBuilder.Path = api;

            // use query string
            if (!useBody)
            {
                var type = paramerters.GetType();
                foreach (var prop in type.GetProperties())
                {
                    var value = prop.GetValue(paramerters, null)?.ToString();
                    if (value != null)
                        query.Add(prop.Name, value);
                }
                uriBuilder.Query = query.ToString();
            }

            var uri = uriBuilder.ToString();

            var requestMessage = new HttpRequestMessage(httpMethod, uri);
            requestMessage.Headers.Add("authorization", AppSettings.AuthHeader);
            if (useBody)
                requestMessage.Content = new StringContent(JsonConvert.SerializeObject(paramerters), Encoding.UTF8, "application/json");

            // send request
            var res = _httpClient.Send(requestMessage);
            using var stream = res.Content.ReadAsStream();
            var streamReader = new StreamReader(stream);
            var ret = streamReader.ReadToEnd();

            if (res.StatusCode != HttpStatusCode.OK)
                throw new Exception(ret);
            return ret;
        }

        private static void GenerateServerAuthHeader(CommandLineApplication cmdApp)
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
                aes.KeySize = 128;
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
                Console.WriteLine("Brearer " + jwt);
            });
        }

        private static void CreateCertificate(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Create a Certificate and add it to the server";
            var serverEndPointOption = cmdApp.Option("-ep|--serverEndPoint", "* Required", CommandOptionType.SingleValue);
            var subjectNameOption = cmdApp.Option("-sn|--subjectName", "Default: random name; example: CN=site.com", CommandOptionType.SingleValue);

            cmdApp.OnExecute(() =>
            {

                var parameters = new
                {
                    serverEndPoint = serverEndPointOption.Value(),
                    subjectName = subjectNameOption.HasValue() ? subjectNameOption.Value() : null
                };
                SendRequest($"Certificate/Create", parameters, HttpMethod.Post, useBody: false);
                Console.WriteLine($"Certificate has been created and assigned to {parameters.serverEndPoint}");
            });
        }

        private static void CreatePublicAccessKey(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Create a public accessKey and add it to the server";
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

                    var accessTokenStr = SendRequest($"AccessToken/CreatePublic", parameters, HttpMethod.Post, useBody: false);
                    dynamic accessToken = JsonConvert.DeserializeObject(accessTokenStr);

                    var accessKey = SendRequest($"AccessToken/GetAccessKey", new { accessToken.accessTokenId }, HttpMethod.Get, useBody: false);
                    Console.WriteLine($"AccessKey\n{accessKey}");

                });
        }
    }
}
