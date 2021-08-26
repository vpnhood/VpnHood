using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Logging;
using VpnHood.Server.Messaging;

namespace VpnHood.Server.AccessServers
{
    public class FileAccessServer : IAccessServer
    {
        public class AccessItem
        {
            public DateTime? ExpirationTime { get; set; }
            public int MaxClientCount { get; set; }
            public long MaxTraffic { get; set; }
            public Token Token { get; set; } = null!;

            [JsonIgnore]
            public AccessUsage AccessUsage { get; set; } = new AccessUsage();
        }

        private class AccessItemUsage
        {
            public long SentTraffic { get; set; }
            public long ReceivedTraffic { get; set; }
        }

        private const string FILEEXT_token = ".token";
        private const string FILEEXT_usage = ".usage";
        private readonly string _sslCertificatesPassword;

        private string GetAccessItemFileName(Guid tokenId) => Path.Combine(StoragePath, tokenId.ToString() + FILEEXT_token);
        private string GetUsageFileName(Guid tokenId) => Path.Combine(StoragePath, tokenId.ToString() + FILEEXT_usage);

        public string StoragePath { get; }

        public FileAccessServerSessionManager SessionManager { get; }
        public bool IsMaintenanceMode { get; } = false; //this server never goes into maintenance mode
        public string CertsFolderPath => Path.Combine(StoragePath, "certificates");
        public string GetCertFilePath(IPEndPoint ipEndPoint) => Path.Combine(CertsFolderPath, ipEndPoint.ToString().Replace(":", "-") + ".pfx");
        public X509Certificate2 DefaultCert { get; }

        public FileAccessServer(string storagePath, string? sslCertificatesPassword = null)
        {
            StoragePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
            _sslCertificatesPassword = sslCertificatesPassword ?? "";
            SessionManager = new FileAccessServerSessionManager();
            Directory.CreateDirectory(StoragePath);

            var defaultCertFile = Path.Combine(CertsFolderPath, "default.pfx");
            DefaultCert = File.Exists(defaultCertFile)
                ? new X509Certificate2(defaultCertFile, sslCertificatesPassword)
                : CreateSelfSignedCertificate(defaultCertFile, sslCertificatesPassword ?? "");
        }

        private static X509Certificate2 CreateSelfSignedCertificate(string certFilePath, string password)
        {
            VhLogger.Instance.LogInformation($"Creating Certificate file: {certFilePath}");
            var certificate = CertificateUtil.CreateSelfSigned();
            var buf = certificate.Export(X509ContentType.Pfx, password);
            Directory.CreateDirectory(Path.GetDirectoryName(certFilePath));
            File.WriteAllBytes(certFilePath, buf);
            return new X509Certificate2(certFilePath, password, X509KeyStorageFlags.Exportable);
        }

        public AccessItem[] AccessItem_LoadAll()
        {
            var files = Directory.GetFiles(StoragePath, "*" + FILEEXT_token);
            return files.Select(x => AccessItem_Read(Guid.Parse(Path.GetFileNameWithoutExtension(x))).Result!).ToArray();
        }

        public AccessItem AccessItem_Create(IPEndPoint publicEndPoint, IPEndPoint? internalEndPoint = null, int maxClientCount = 1,
            string? tokenName = null, int maxTrafficByteCount = 0, DateTime? expirationTime = null)
        {
            // find or create the certificate
            var certificate = DefaultCert;
            if (internalEndPoint != null)
            {
                var certFilePath = GetCertFilePath(internalEndPoint);
                certificate = File.Exists(certFilePath)
                    ? new X509Certificate2(certFilePath, _sslCertificatesPassword)
                    : CreateSelfSignedCertificate(certFilePath, _sslCertificatesPassword);
            }

            // generate key
            var aes = Aes.Create();
            aes.KeySize = 128;
            aes.GenerateKey();

            // create AccessItem
            var accessItem = new AccessItem()
            {
                MaxTraffic = maxTrafficByteCount,
                MaxClientCount = maxClientCount,
                ExpirationTime = expirationTime,
                Token = new Token(secret: aes.Key,
                                  certificateHash: certificate.GetCertHash(),
                                  hostName: certificate.GetNameInfo(X509NameType.DnsName, false)
                                  )
                {
                    Name = tokenName,
                    HostPort = publicEndPoint.Port,
                    HostEndPoint = publicEndPoint,
                    TokenId = Guid.NewGuid(),
                    SupportId = 0,
                    IsValidHostName = false,
                }
            };

            var token = accessItem.Token;

            // Write accessItem
            File.WriteAllText(GetAccessItemFileName(token.TokenId), JsonSerializer.Serialize(accessItem));

            // buidl default usage
            ReadAccessItemUsage(accessItem).Wait(); 
            WriteAccessItemUsage(accessItem).Wait();

            return accessItem;
        }

        public async Task AccessItem_Delete(Guid tokenId)
        {
            // remove index
            var accessItem = await AccessItem_Read(tokenId);
            if (accessItem == null)
                throw new KeyNotFoundException("Could not find tokenId");

            // delete files
            if (File.Exists(GetUsageFileName(tokenId)))
                File.Delete(GetUsageFileName(tokenId));
            if (File.Exists(GetAccessItemFileName(tokenId)))
                File.Delete(GetAccessItemFileName(tokenId));
        }

        public async Task<AccessItem?> AccessItem_Read(Guid tokenId)
        {
            // read access item
            var accessItemPath = GetAccessItemFileName(tokenId);
            if (!File.Exists(accessItemPath))
                return null;

            var json = await File.ReadAllTextAsync(accessItemPath);
            var accessItem = Util.JsonDeserialize<AccessItem>(json);
            await ReadAccessItemUsage(accessItem);
            return accessItem;
        }

        private async Task ReadAccessItemUsage(AccessItem accessItem)
        {
            // read usageItem
            accessItem.AccessUsage = new AccessUsage()
            {
                ExpirationTime = accessItem.ExpirationTime,
                MaxClientCount = accessItem.MaxClientCount,
                MaxTraffic = accessItem.MaxTraffic,
                ActiveClientCount = 0,
            };

            // update usage
            try
            {
                var usagePath = GetUsageFileName(accessItem.Token.TokenId);
                if (File.Exists(usagePath))
                {
                    var json = await File.ReadAllTextAsync(usagePath);
                    var accessItemUsage = JsonSerializer.Deserialize<AccessItemUsage>(json) ?? new AccessItemUsage();
                    accessItem.AccessUsage.ReceivedTraffic = accessItemUsage.ReceivedTraffic;
                    accessItem.AccessUsage.SentTraffic = accessItemUsage.SentTraffic;
                }
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogWarning($"Error in reading AccesUsage of token: {accessItem.Token.TokenId}, Message: {ex.Message}");
            }
        }

        private Task WriteAccessItemUsage(AccessItem accessItem)
        {
            // write token info
            var accessItemUsage = new AccessItemUsage()
            {
                ReceivedTraffic = accessItem.AccessUsage.ReceivedTraffic,
                SentTraffic = accessItem.AccessUsage.SentTraffic
            };
            var json = JsonSerializer.Serialize(accessItemUsage);
            return File.WriteAllTextAsync(GetUsageFileName(accessItem.Token.TokenId), json);
        }

        private X509Certificate2 GetSslCertificate(IPEndPoint hostEndPoint, bool returnDefaultIfNotFound)
        {
            var certFilePath = GetCertFilePath(hostEndPoint);
            if (returnDefaultIfNotFound && !File.Exists(certFilePath))
                return DefaultCert;
            return new X509Certificate2(certFilePath, _sslCertificatesPassword, X509KeyStorageFlags.Exportable);
        }

        public ServerStatus? ServerStatus { get; private set; }
        public Task Server_SetStatus(ServerStatus serverStatus)
        {
            ServerStatus = serverStatus;
            return Task.FromResult(0);
        }

        public ServerInfo? SubscribedServerInfo { get; private set; }
        public Task Server_Subscribe(ServerInfo serverInfo)
        {
            SubscribedServerInfo = serverInfo;
            return Task.FromResult(0);
        }

        public Task<byte[]> GetSslCertificateData(IPEndPoint hostEndPoint)
            => Task.FromResult(GetSslCertificate(hostEndPoint, true).Export(X509ContentType.Pfx));

        public async Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx)
        {
            var accessItem = await AccessItem_Read(sessionRequestEx.TokenId);
            if (accessItem == null)
                return new(SessionErrorCode.GeneralError) { ErrorMessage = "Token does not exist!" };

            return SessionManager.CreateSession(sessionRequestEx, accessItem);
        }

        public async Task<SessionResponseEx> Session_Get(uint sessionId, IPEndPoint hostEndPoint, IPAddress? clientIp)
        {
            _ = hostEndPoint;
            _ = clientIp;

            // find token
            var tokenId = SessionManager.TokenIdFromSessionId(sessionId);
            if (tokenId == null)
                return new(SessionErrorCode.GeneralError) { ErrorMessage = "Session does not exist!" };

            // read accessItem
            var accessItem = await AccessItem_Read(tokenId.Value);
            if (accessItem == null)
                return new(SessionErrorCode.GeneralError) {ErrorMessage = "Token does not exist!" };

            // read usage
            return SessionManager.GetSession(sessionId, accessItem, hostEndPoint);
        }


        public async Task<ResponseBase> Session_AddUsage(uint sessionId, bool closeSession, UsageInfo usageInfo)
        {
            // find token
            var tokenId = SessionManager.TokenIdFromSessionId(sessionId);
            if (tokenId == null)
                return new(SessionErrorCode.GeneralError) { ErrorMessage = "Session does not exist!" };

            // read accessItem
            var accessItem = await AccessItem_Read(tokenId.Value);
            if (accessItem == null)
                return new(SessionErrorCode.GeneralError) { ErrorMessage = "Token does not exist!" };

            accessItem.AccessUsage.SentTraffic += usageInfo.SentTraffic;
            accessItem.AccessUsage.ReceivedTraffic += usageInfo.ReceivedTraffic;
            await WriteAccessItemUsage(accessItem);

            if (closeSession)
                SessionManager.CloseSession(sessionId);

            var res = SessionManager.GetSession(sessionId, accessItem, null);
            var ret = new ResponseBase(res.ErrorCode)
            {
                AccessUsage = res.AccessUsage,
                ErrorMessage = res.ErrorMessage,
                SuppressedBy = res.SuppressedBy
            };

            return ret;
        }

        public void Dispose()
        {
            SessionManager.Dispose();
        }
    }
}
