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
using Microsoft.Extensions.Logging;
using VpnHood.Common;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Server.Messaging;

namespace VpnHood.Server.Providers.FileAccessServerProvider;

public class FileAccessServer : IAccessServer
{
    private const string FileExtToken = ".token";
    private const string FileExtUsage = ".usage";
    private readonly string _sslCertificatesPassword;
    public ServerConfig ServerConfig { get; }

    public FileAccessServer(string storagePath, FileAccessServerOptions options)
    {
        using var scope = VhLogger.Instance.BeginScope("FileAccessServer");

        StoragePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
        ServerConfig = options;
        _sslCertificatesPassword = options.SslCertificatesPassword ?? "";
        SessionManager = new FileAccessServerSessionManager();
        Directory.CreateDirectory(StoragePath);

        var defaultCertFile = Path.Combine(CertsFolderPath, "default.pfx");
        DefaultCert = File.Exists(defaultCertFile)
            ? new X509Certificate2(defaultCertFile, _sslCertificatesPassword, X509KeyStorageFlags.Exportable)
            : CreateSelfSignedCertificate(defaultCertFile, _sslCertificatesPassword);
    }

    public string StoragePath { get; }

    public FileAccessServerSessionManager SessionManager { get; }
    public string CertsFolderPath => Path.Combine(StoragePath, "certificates");
    public X509Certificate2 DefaultCert { get; }

    public ServerStatus? ServerStatus { get; private set; }

    public ServerInfo? ServerInfo { get; private set; }
    public bool IsMaintenanceMode => false; //this server never goes into maintenance mode

    public Task<ServerCommand> Server_UpdateStatus(ServerStatus serverStatus)
    {
        ServerStatus = serverStatus;
        return Task.FromResult(new ServerCommand(ServerConfig.ConfigCode));
    }

    public Task<ServerConfig> Server_Configure(ServerInfo serverInfo)
    {
        ServerInfo = serverInfo;
        ServerStatus = serverInfo.Status;
        return Task.FromResult(ServerConfig);
    }

    public Task<byte[]> GetSslCertificateData(IPEndPoint hostEndPoint)
    {
        var cert = GetSslCertificate(hostEndPoint, true).Export(X509ContentType.Pfx);
        return Task.FromResult(cert);
    }

    public async Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx)
    {
        var accessItem = await AccessItem_Read(sessionRequestEx.TokenId);
        if (accessItem == null)
            return new SessionResponseEx(SessionErrorCode.AccessError) { ErrorMessage = "Token does not exist." };

        return SessionManager.CreateSession(sessionRequestEx, accessItem);
    }

    public async Task<SessionResponseEx> Session_Get(uint sessionId, IPEndPoint hostEndPoint, IPAddress? clientIp)
    {
        _ = hostEndPoint;
        _ = clientIp;

        // find token
        var tokenId = SessionManager.TokenIdFromSessionId(sessionId);
        if (tokenId == null)
            return new SessionResponseEx(SessionErrorCode.AccessError)
            {
                SessionId = sessionId,
                ErrorMessage = "Session does not exist."
            };

        // read accessItem
        var accessItem = await AccessItem_Read(tokenId.Value);
        if (accessItem == null)
            return new SessionResponseEx(SessionErrorCode.AccessError)
            {
                SessionId = sessionId,
                ErrorMessage = "Token does not exist."
            };

        // read usage
        return SessionManager.GetSession(sessionId, accessItem, hostEndPoint);
    }

    public Task<SessionResponseBase> Session_AddUsage(uint sessionId, UsageInfo usageInfo)
    {
        return Session_AddUsage(sessionId, usageInfo, false);
    }

    public Task<SessionResponseBase> Session_Close(uint sessionId, UsageInfo usageInfo)
    {
        return Session_AddUsage(sessionId, usageInfo, true);
    }

    private async Task<SessionResponseBase> Session_AddUsage(uint sessionId, UsageInfo usageInfo, bool closeSession)
    {
        // find token
        var tokenId = SessionManager.TokenIdFromSessionId(sessionId);
        if (tokenId == null)
            return new SessionResponseBase(SessionErrorCode.AccessError) { ErrorMessage = "Token does not exist." };

        // read accessItem
        var accessItem = await AccessItem_Read(tokenId.Value);
        if (accessItem == null)
            return new SessionResponseBase(SessionErrorCode.AccessError) { ErrorMessage = "Token does not exist." };

        accessItem.AccessUsage.SentTraffic += usageInfo.SentTraffic;
        accessItem.AccessUsage.ReceivedTraffic += usageInfo.ReceivedTraffic;
        await WriteAccessItemUsage(accessItem);

        if (closeSession)
            SessionManager.CloseSession(sessionId);

        var res = SessionManager.GetSession(sessionId, accessItem, null);
        var ret = new SessionResponseBase(res.ErrorCode)
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

    private string GetAccessItemFileName(Guid tokenId)
    {
        return Path.Combine(StoragePath, tokenId + FileExtToken);
    }

    private string GetUsageFileName(Guid tokenId)
    {
        return Path.Combine(StoragePath, tokenId + FileExtUsage);
    }

    public string GetCertFilePath(IPEndPoint ipEndPoint)
    {
        return Path.Combine(CertsFolderPath, ipEndPoint.ToString().Replace(":", "-") + ".pfx");
    }

    private static X509Certificate2 CreateSelfSignedCertificate(string certFilePath, string password)
    {
        VhLogger.Instance.LogInformation($"Creating Certificate file: {certFilePath}");
        var certificate = CertificateUtil.CreateSelfSigned();
        var buf = certificate.Export(X509ContentType.Pfx, password);
        Directory.CreateDirectory(Path.GetDirectoryName(certFilePath)!);
        File.WriteAllBytes(certFilePath, buf);
        return new X509Certificate2(certFilePath, password, X509KeyStorageFlags.Exportable);
    }

    public AccessItem[] AccessItem_LoadAll()
    {
        var files = Directory.GetFiles(StoragePath, "*" + FileExtToken);
        return files.Select(x => AccessItem_Read(Guid.Parse(Path.GetFileNameWithoutExtension(x))).Result!)
            .ToArray();
    }

    public AccessItem AccessItem_Create(IPEndPoint[] publicEndPoints,
        int maxClientCount = 1,
        string? tokenName = null,
        int maxTrafficByteCount = 0,
        DateTime? expirationTime = null,
        bool isValidHostName = false,
        int hostPort = 443)
    {
        // find or create the certificate
        var certificate = DefaultCert;

        // generate key
        var aes = Aes.Create();
        aes.KeySize = 128;
        aes.GenerateKey();

        // create AccessItem
        var accessItem = new AccessItem
        {
            MaxTraffic = maxTrafficByteCount,
            MaxClientCount = maxClientCount,
            ExpirationTime = expirationTime,
            Token = new Token(aes.Key,
                certificate.GetCertHash(),
                certificate.GetNameInfo(X509NameType.DnsName, false) ??
                throw new Exception("Certificate must have a subject!")
            )
            {
                Name = tokenName,
                HostPort = hostPort,
                HostEndPoints = publicEndPoints,
                TokenId = Guid.NewGuid(),
                SupportId = 0,
                IsValidHostName = isValidHostName
            }
        };

        var token = accessItem.Token;

        // Write accessItem
        File.WriteAllText(GetAccessItemFileName(token.TokenId), JsonSerializer.Serialize(accessItem));

        // build default usage
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
        var fileName = GetAccessItemFileName(tokenId);
        string json;
        using (await AsyncLock.LockAsync(fileName).ConfigureAwait(false))
        {
            if (!File.Exists(fileName))
                return null;

            json = await File.ReadAllTextAsync(fileName);
        }
        var accessItem = Util.JsonDeserialize<AccessItem>(json);
        await ReadAccessItemUsage(accessItem);
        return accessItem;
    }

    private async Task ReadAccessItemUsage(AccessItem accessItem)
    {
        // read usageItem
        accessItem.AccessUsage = new AccessUsage
        {
            ExpirationTime = accessItem.ExpirationTime,
            MaxClientCount = accessItem.MaxClientCount,
            MaxTraffic = accessItem.MaxTraffic,
            ActiveClientCount = 0
        };

        // update usage
        try
        {
            var fileName = GetUsageFileName(accessItem.Token.TokenId);
            using (await AsyncLock.LockAsync(fileName).ConfigureAwait(false))
            {
                if (File.Exists(fileName))
                {
                    var json = await File.ReadAllTextAsync(fileName);
                    var accessItemUsage = JsonSerializer.Deserialize<AccessItemUsage>(json) ?? new AccessItemUsage();
                    accessItem.AccessUsage.ReceivedTraffic = accessItemUsage.ReceivedTraffic;
                    accessItem.AccessUsage.SentTraffic = accessItemUsage.SentTraffic;
                }
            }
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning(
                $"Error in reading AccessUsage of token: {accessItem.Token.TokenId}, Message: {ex.Message}");
        }
    }

    private async Task WriteAccessItemUsage(AccessItem accessItem)
    {
        // write token info
        var accessItemUsage = new AccessItemUsage
        {
            ReceivedTraffic = accessItem.AccessUsage.ReceivedTraffic,
            SentTraffic = accessItem.AccessUsage.SentTraffic
        };
        var json = JsonSerializer.Serialize(accessItemUsage);

        // write accessItem
        var fileName = GetUsageFileName(accessItem.Token.TokenId);
        using (await AsyncLock.LockAsync(fileName).ConfigureAwait(false))
        {
            await File.WriteAllTextAsync(fileName, json);
        }
    }

    private X509Certificate2 GetSslCertificate(IPEndPoint hostEndPoint, bool returnDefaultIfNotFound)
    {
        var certFilePath = GetCertFilePath(hostEndPoint);
        if (returnDefaultIfNotFound && !File.Exists(certFilePath))
            return DefaultCert;
        return new X509Certificate2(certFilePath, _sslCertificatesPassword, X509KeyStorageFlags.Exportable);
    }

    public class AccessItem
    {
        public DateTime? ExpirationTime { get; set; }
        public int MaxClientCount { get; set; }
        public long MaxTraffic { get; set; }
        public Token Token { get; set; } = null!;

        [JsonIgnore] public AccessUsage AccessUsage { get; set; } = new();
    }

    private class AccessItemUsage
    {
        public long SentTraffic { get; set; }
        public long ReceivedTraffic { get; set; }
    }
}