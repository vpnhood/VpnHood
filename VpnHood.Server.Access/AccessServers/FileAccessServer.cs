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
            public Token Token { get; set; }
        }

        private class Usage
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

        public Guid TokenIdFromSupportId(int supportId) => _supportIdIndex[supportId];
        public ClientIdentity ClientIdentityFromSupportId(int supportId) => ClientIdentityFromTokenId(TokenIdFromSupportId(supportId));
        public ClientIdentity ClientIdentityFromTokenId(Guid tokenId) => new ClientIdentity() { TokenId = tokenId };
        public string CertsFolderPath => Path.Combine(StoragePath, "certificates");
        public string GetCertFilePath(IPEndPoint ipEndPoint) => Path.Combine(CertsFolderPath, ipEndPoint.ToString().Replace(":", "-") + ".pfx");
        public X509Certificate2 DefaultCert { get; }

        public FileAccessServer(string storagePath, string sslCertificatesPassword = null)
        {
            StoragePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
            _sslCertificatesPassword = sslCertificatesPassword;
            _supportIdIndex = LoadSupportIdIndex(FILEPATH_SupportIdIndex);
            Directory.CreateDirectory(StoragePath);

            var defaultCertFile = Path.Combine(CertsFolderPath, "default.pfx");
            DefaultCert = File.Exists(defaultCertFile)
                ? new X509Certificate2(defaultCertFile, sslCertificatesPassword)
                : CreateSelfSignedCertificate(defaultCertFile, sslCertificatesPassword);
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
            _supportIdIndex.Remove(accessItem.Token.SupportId);

            // delete files
            File.Delete(GetUsageFileName(tokenId));
            File.Delete(GetAccessItemFileName(tokenId));

            // remove index
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

        public AccessItem CreateAccessItem(IPEndPoint publicEndPoint, IPEndPoint internalEndPoint = null, int maxClientCount = 1,
            string tokenName = null, int maxTrafficByteCount = 0, DateTime? expirationTime = null)
        {
            // find or create the certificate
            X509Certificate2 certificate = DefaultCert;
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
                Token = new Token()
                {
                    Name = tokenName,
                    TokenId = Guid.NewGuid(),
                    ServerEndPoint = publicEndPoint.ToString(),
                    Secret = aes.Key,
                    DnsName = certificate.GetNameInfo(X509NameType.DnsName, false),
                    CertificateHash = certificate.GetCertHash(),
                    SupportId = GetNewTokenSupportId()
                }
            };

            // write usage
            var token = accessItem.Token;
            Usage_Write(token.TokenId, new Usage());

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

        private Task Usage_Write(Guid tokenId, Usage usage)
        {
            // write token info
            var json = JsonSerializer.Serialize(usage);
            return File.WriteAllTextAsync(GetUsageFileName(tokenId), json);
        }

        public Guid[] GetAllTokenIds() => _supportIdIndex.Select(x => x.Value).ToArray();
        private int GetNewTokenSupportId()
        {
            return _supportIdIndex.Count == 0 ? 1 : _supportIdIndex.Max(x => x.Key) + 1;
        }

        public async Task<AccessItem> AccessItem_Read(Guid tokenId)
        {
            // read access item
            var accessItemPath = GetAccessItemFileName(tokenId);
            if (!File.Exists(accessItemPath))
                return null;
            var json = await File.ReadAllTextAsync(accessItemPath);
            return JsonSerializer.Deserialize<AccessItem>(json);
        }

        private async Task<Usage> Usage_Read(Guid tokenId)
        {
            // read usageItem
            var usage = new Usage();
            var usagePath = GetUsageFileName(tokenId);
            if (File.Exists(usagePath))
            {
                var json = await File.ReadAllTextAsync(usagePath);
                usage = JsonSerializer.Deserialize<Usage>(json);
            }
            return usage;
        }

        public async Task<Access> GetAccess(ClientIdentity clientIdentity)
        {
            if (clientIdentity is null) throw new ArgumentNullException(nameof(clientIdentity));
            var usage = await Usage_Read(clientIdentity.TokenId);
            return await GetAccess(clientIdentity, usage);
        }

        private async Task<Access> GetAccess(ClientIdentity clientIdentity, Usage usage)
        {
            if (clientIdentity is null) throw new ArgumentNullException(nameof(clientIdentity));
            if (usage is null) throw new ArgumentNullException(nameof(usage));

            var tokenId = clientIdentity.TokenId;
            var accessItem = await AccessItem_Read(tokenId);
            if (accessItem == null)
                return null;

            var access = new Access()
            {
                AccessId = clientIdentity.TokenId.ToString(),
                DnsName = accessItem.Token.DnsName,
                ExpirationTime = accessItem.ExpirationTime,
                MaxClientCount = accessItem.MaxClientCount,
                MaxTrafficByteCount = accessItem.MaxTrafficByteCount,
                Secret = accessItem.Token.Secret,
                ReceivedTrafficByteCount = usage.ReceivedTrafficByteCount,
                RedirectServerEndPoint = accessItem.Token.ServerEndPoint,
                SentTrafficByteCount = usage.SentTrafficByteCount,
                StatusCode = AccessStatusCode.Ok,
            };

            if (accessItem.MaxTrafficByteCount != 0 && usage.SentTrafficByteCount + usage.ReceivedTrafficByteCount > accessItem.MaxTrafficByteCount)
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

        public async Task<Access> AddUsage(AddUsageParams addUsageParams)
        {
            var clientIdentity = addUsageParams.ClientIdentity ?? throw new ArgumentNullException(nameof(AddUsageParams.ClientIdentity));

            // write usage
            var tokenId = clientIdentity.TokenId;
            var usage = await Usage_Read(tokenId);
            usage.SentTrafficByteCount += addUsageParams.SentTrafficByteCount;
            usage.ReceivedTrafficByteCount += addUsageParams.ReceivedTrafficByteCount;
            await Usage_Write(tokenId, usage);

            return await GetAccess(clientIdentity, usage);
        }

        private X509Certificate2 GetSslCertificate(IPEndPoint serverEndPoint, bool returnDefaultIfNotFound)
        {
            var certFilePath = GetCertFilePath(serverEndPoint);
            if (returnDefaultIfNotFound && !File.Exists(certFilePath))
                return DefaultCert;
            return new X509Certificate2(certFilePath, _sslCertificatesPassword, X509KeyStorageFlags.Exportable);
        }

        public Task<byte[]> GetSslCertificateData(string serverEndPoint)
            => Task.FromResult(GetSslCertificate(Util.ParseIpEndPoint(serverEndPoint), true).Export(X509ContentType.Pfx));

        public Task SendServerStatus(ServerStatus serverStatus)
            => Task.FromResult(0);
    }
}
