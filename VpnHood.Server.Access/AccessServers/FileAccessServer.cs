using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;
using VpnHood.Common;
using VpnHood.Common.Converters;
using VpnHood.Logging;

namespace VpnHood.Server.AccessServers
{
    public class FileAccessServer : IAccessServer
    {
        public class AccessItem
        {
            public DateTime? ExpirationTime { get; set; }
            public int MaxClientCount { get; set; }
            public long MaxTrafficByteCount { get; set; }
            public Token Token { get; set; } = null!;
        }

        private class AccessItemUsage
        {
            public long SentTrafficByteCount { get; set; }
            public long ReceivedTrafficByteCount { get; set; }
        }

        private const string FILEEXT_token = ".token";
        private const string FILEEXT_usage = ".usage";
        private const string FILENAME_SupportIdIndex = "supportId.index";
        private readonly Dictionary<int, Guid> _supportIdIndex;
        private readonly string _sslCertificatesPassword;
        private string FILEPATH_SupportIdIndex => Path.Combine(StoragePath, FILENAME_SupportIdIndex);
        private string GetAccessItemFileName(Guid tokenId) => Path.Combine(StoragePath, tokenId.ToString() + FILEEXT_token);
        private string GetUsageFileName(Guid tokenId) => Path.Combine(StoragePath, tokenId.ToString() + FILEEXT_usage);
        public string StoragePath { get; }

        public bool IsMaintenanceMode { get; } = false; //this server never goes into maintenance mode
        public Guid TokenIdFromSupportId(int supportId) => _supportIdIndex[supportId];
        public string CertsFolderPath => Path.Combine(StoragePath, "certificates");
        public string GetCertFilePath(IPEndPoint ipEndPoint) => Path.Combine(CertsFolderPath, ipEndPoint.ToString().Replace(":", "-") + ".pfx");
        public X509Certificate2 DefaultCert { get; }

        public FileAccessServer(string storagePath, string? sslCertificatesPassword = null)
        {
            StoragePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
            _sslCertificatesPassword = sslCertificatesPassword ?? "";
            _supportIdIndex = LoadSupportIdIndex(FILEPATH_SupportIdIndex);
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

        public async Task RemoveToken(Guid tokenId)
        {
            // remove index
            var accessItem = await AccessItem_Read(tokenId);
            if (accessItem == null)
                throw new KeyNotFoundException("Could not find tokenId");

            // delete files
            File.Delete(GetUsageFileName(tokenId));
            File.Delete(GetAccessItemFileName(tokenId));

            // remove support index
            _supportIdIndex.Remove(accessItem.Token.SupportId);
            WriteSupportIdIndex();
        }

        private Dictionary<int, Guid> LoadSupportIdIndex(string fileName)
        {
            var ret = new Dictionary<int, Guid>();
            try
            {
                if (File.Exists(fileName))
                {
                    var json = File.ReadAllText(fileName);
                    var collection = JsonSerializer.Deserialize<KeyValuePair<int, Guid>[]>(json);
                    ret = new Dictionary<int, Guid>(collection);
                }
            }
            catch { }

            return ret;
        }

        public AccessItem CreateAccessItem(IPEndPoint publicEndPoint, IPEndPoint? internalEndPoint = null, int maxClientCount = 1,
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
                MaxTrafficByteCount = maxTrafficByteCount,
                MaxClientCount = maxClientCount,
                ExpirationTime = expirationTime,
                Token = new Token(secret: aes.Key,
                                  certificateHash: certificate.GetCertHash(),
                                  serverAuthority: certificate.GetNameInfo(X509NameType.DnsName, false) + ":" + publicEndPoint.Port.ToString()
                                  )
                {
                    Name = tokenName,
                    ServerEndPoint = publicEndPoint,
                    TokenId = Guid.NewGuid(),
                    SupportId = GetNewTokenSupportId(),
                    IsValidServerAuthority = false,
                }
            };

            // write accessItemUsage
            var token = accessItem.Token;
            Usage_Write(token.TokenId, new AccessItemUsage());

            // Write accessItem
            File.WriteAllText(GetAccessItemFileName(token.TokenId), JsonSerializer.Serialize(accessItem));

            // update index
            _supportIdIndex.Add(token.SupportId, token.TokenId);
            WriteSupportIdIndex();

            return accessItem;
        }

        private void WriteSupportIdIndex()
        {
            var arr = _supportIdIndex.ToArray();
            File.WriteAllText(FILEPATH_SupportIdIndex, JsonSerializer.Serialize(arr));
        }

        private Task Usage_Write(Guid tokenId, AccessItemUsage accessItemUsage)
        {
            // write token info
            var json = JsonSerializer.Serialize(accessItemUsage);
            return File.WriteAllTextAsync(GetUsageFileName(tokenId), json);
        }

        public Guid[] GetAllTokenIds() => _supportIdIndex.Select(x => x.Value).ToArray();
        private int GetNewTokenSupportId()
        {
            return _supportIdIndex.Count == 0 ? 1 : _supportIdIndex.Max(x => x.Key) + 1;
        }

        public async Task<AccessItem?> AccessItem_Read(Guid tokenId)
        {
            // read access item
            var accessItemPath = GetAccessItemFileName(tokenId);
            if (!File.Exists(accessItemPath))
                return null;

            var json = await File.ReadAllTextAsync(accessItemPath);
            return JsonSerializer.Deserialize<AccessItem>(json);
        }

        private async Task<AccessItemUsage> Usage_Read(Guid tokenId)
        {
            // read usageItem
            var accessItemUsage = new AccessItemUsage();
            var usagePath = GetUsageFileName(tokenId);
            if (File.Exists(usagePath))
            {
                var json = await File.ReadAllTextAsync(usagePath);
                accessItemUsage = JsonSerializer.Deserialize<AccessItemUsage>(json) ?? new AccessItemUsage();
            }
            return accessItemUsage;
        }

        public async Task<Access> GetAccess(AccessRequest accessRequest)
        {
            var clientInfo = accessRequest.ClientInfo;
            if (clientInfo is null) throw new ArgumentNullException(nameof(clientInfo));
            var accessItemUsage = await Usage_Read(accessRequest.TokenId);
            return await GetAccess(accessRequest.TokenId, accessItemUsage);
        }

        private async Task<Access> GetAccess(Guid tokenId, AccessItemUsage accessItemUsage)
        {
            if (accessItemUsage is null) throw new ArgumentNullException(nameof(accessItemUsage));

            var accessItem = await AccessItem_Read(tokenId);
            if (accessItem == null)
                return new Access(accessId: "", secret: Array.Empty<byte>(), "") { StatusCode = AccessStatusCode.Error, Message = "Token does not exist!" };

            var access = new Access(accessId: tokenId.ToString(), secret: accessItem.Token.Secret, dnsName: accessItem.Token.ServerAuthority)
            {
                ExpirationTime = accessItem.ExpirationTime,
                MaxClientCount = accessItem.MaxClientCount,
                MaxTrafficByteCount = accessItem.MaxTrafficByteCount,
                ReceivedTrafficByteCount = accessItemUsage.ReceivedTrafficByteCount,
                RedirectServerEndPoint = null,
                SentTrafficByteCount = accessItemUsage.SentTrafficByteCount,
                StatusCode = AccessStatusCode.Ok,
            };

            if (accessItem.MaxTrafficByteCount != 0 && accessItemUsage.SentTrafficByteCount + accessItemUsage.ReceivedTrafficByteCount > accessItem.MaxTrafficByteCount)
            {
                access.Message = "All traffic has been consumed!";
                access.StatusCode = AccessStatusCode.TrafficOverflow;
            }

            if (accessItem.ExpirationTime != null && accessItem.ExpirationTime < DateTime.Now)
            {
                access.Message = "Access Expired!";
                access.StatusCode = AccessStatusCode.Expired;
            }

            return access;
        }

        public async Task<Access> AddUsage(string accessId, UsageInfo usageInfo)
        {
            var tokenId = Guid.Parse(accessId);

            // write accessItemUsage
            var accessItemUsage = await Usage_Read(tokenId);
            accessItemUsage.SentTrafficByteCount += usageInfo.SentTrafficByteCount;
            accessItemUsage.ReceivedTrafficByteCount += usageInfo.ReceivedTrafficByteCount;
            await Usage_Write(tokenId, accessItemUsage);

            return await GetAccess(tokenId, accessItemUsage);
        }

        private X509Certificate2 GetSslCertificate(IPEndPoint serverEndPoint, bool returnDefaultIfNotFound)
        {
            var certFilePath = GetCertFilePath(serverEndPoint);
            if (returnDefaultIfNotFound && !File.Exists(certFilePath))
                return DefaultCert;
            return new X509Certificate2(certFilePath, _sslCertificatesPassword, X509KeyStorageFlags.Exportable);
        }

        public Task<byte[]> GetSslCertificateData(string serverEndPoint)
            => Task.FromResult(GetSslCertificate(IPEndPointConverter.Parse(serverEndPoint), true).Export(X509ContentType.Pfx));

        public ServerStatus? ServerStatus { get; private set; }
        public Task SendServerStatus(ServerStatus serverStatus)
        {
            ServerStatus = serverStatus;
            return Task.FromResult(0);
        }

        public ServerInfo? SubscribedServerInfo { get; private set; }
        public Task SubscribeServer(ServerInfo serverInfo)
        {
            SubscribedServerInfo = serverInfo;
            return Task.FromResult(0);
        }

        public void Dispose()
        {
        }
    }
}
