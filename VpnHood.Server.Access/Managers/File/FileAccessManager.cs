using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VpnHood.Common;
using VpnHood.Common.IpLocations;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Server.Access.Configurations;
using VpnHood.Server.Access.Messaging;

namespace VpnHood.Server.Access.Managers.File;

public class FileAccessManager : IAccessManager
{
    private const string FileExtToken = ".token";
    private const string FileExtUsage = ".usage";
    private ServerToken _serverToken;
    public FileAccessManagerOptions ServerConfig { get; }
    public string StoragePath { get; }
    public FileAccessManagerSessionController SessionController { get; }
    public string CertsFolderPath => Path.Combine(StoragePath, "certificates");
    public string SessionsFolderPath => Path.Combine(StoragePath, "sessions");
    public X509Certificate2 DefaultCert { get; }
    public ServerStatus? ServerStatus { get; private set; }
    public ServerInfo? ServerInfo { get; private set; }
    public bool IsMaintenanceMode => false; //this server never goes into maintenance mode

    public FileAccessManager(string storagePath, FileAccessManagerOptions options)
    {
        using var scope = VhLogger.Instance.BeginScope("FileAccessManager");

        StoragePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
        ServerConfig = options;
        SessionController = new FileAccessManagerSessionController(SessionsFolderPath);
        Directory.CreateDirectory(StoragePath);

        var defaultCertFile = Path.Combine(CertsFolderPath, "default.pfx");
        DefaultCert = System.IO.File.Exists(defaultCertFile)
            ? new X509Certificate2(defaultCertFile, options.SslCertificatesPassword ?? string.Empty, X509KeyStorageFlags.Exportable)
            : CreateSelfSignedCertificate(defaultCertFile, options.SslCertificatesPassword ?? string.Empty);

        ServerConfig.Certificates =
        [
            new CertificateData
            {
                CommonName = DefaultCert.GetNameInfo(X509NameType.DnsName, false) ?? throw new Exception("Could not get the HostName from the certificate."),
                RawData = DefaultCert.Export(X509ContentType.Pfx)
            }
        ];

        // get or create server secret
        ServerConfig.ServerSecret ??= LoadServerSecret();

        // get server token
        _serverToken = GetAndUpdateServerToken();
    }

    public void ClearCache()
    {
        _serverToken = GetAndUpdateServerToken();
    }

    private ServerToken GetAndUpdateServerToken() => GetAndUpdateServerToken(ServerConfig, DefaultCert, Path.Combine(StoragePath, "server-token", "enc-server-token"));
    private ServerToken GetAndUpdateServerToken(FileAccessManagerOptions serverConfig, X509Certificate2 certificate, string encServerTokenFilePath)
    {
        // PublicEndPoints
        var publicEndPoints = serverConfig.PublicEndPoints ?? serverConfig.TcpEndPointsValue;
        if (!publicEndPoints.Any() || publicEndPoints.Any(x => x.Address.Equals(IPAddress.Any) || x.Address.Equals(IPAddress.IPv6Any)))
            throw new Exception("PublicEndPoints has not been configured properly.");


        var serverLocation = LoadServerLocation().Result;
        var serverToken = new ServerToken
        {
            CertificateHash = serverConfig.IsValidHostName ? null : certificate.GetCertHash(),
            HostPort = serverConfig.HostPort ?? publicEndPoints.FirstOrDefault()?.Port ?? 443,
            HostEndPoints = publicEndPoints,
            HostName = certificate.GetNameInfo(X509NameType.DnsName, false) ?? throw new Exception("Certificate must have a subject!"),
            IsValidHostName = serverConfig.IsValidHostName,
            Secret = serverConfig.ServerSecretValue,
            Url = serverConfig.ServerTokenUrl,
            CreatedTime = VhUtil.RemoveMilliseconds(DateTime.UtcNow),
            ServerLocations = serverLocation != null ? [serverLocation] : null
        };

        // write encrypted server token
        if (System.IO.File.Exists(encServerTokenFilePath))
            try
            {
                var oldServerToken = ServerToken.Decrypt(serverConfig.ServerSecret ?? new byte[16], System.IO.File.ReadAllText(encServerTokenFilePath));
                if (!oldServerToken.IsTokenUpdated(serverToken))
                    return oldServerToken;
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogWarning(ex, "Error in reading enc-server-token.");
            }

        Directory.CreateDirectory(Path.GetDirectoryName(encServerTokenFilePath)!);
        System.IO.File.WriteAllText(encServerTokenFilePath, serverToken.Encrypt());
        return serverToken;
    }

    private byte[] LoadServerSecret()
    {
        var serverSecretFile = Path.Combine(CertsFolderPath, "secret");
        var secretBase64 = TryToReadFile(serverSecretFile);
        if (string.IsNullOrEmpty(secretBase64))
        {
            secretBase64 = Convert.ToBase64String(VhUtil.GenerateKey(128));
            System.IO.File.WriteAllText(serverSecretFile, secretBase64);
        }

        return Convert.FromBase64String(secretBase64);
    }

    private async Task<string?> LoadServerLocation()
    {
        try
        {
            var serverCountryFile = Path.Combine(StoragePath, "server_location");
            var serverLocation = TryToReadFile(serverCountryFile);
            if (string.IsNullOrEmpty(serverLocation) && ServerConfig.UseExternalLocationService)
            {
                var ipLocationProvider = new IpLocationProviderFactory().CreateDefault("VpnHood-Server");
                var ipLocation = await ipLocationProvider.GetLocation(new HttpClient()).VhConfigureAwait();
                serverLocation = IpLocationProviderFactory.GetPath(ipLocation.CountryCode, ipLocation.RegionName, ipLocation.CityName);
                await System.IO.File.WriteAllTextAsync(serverCountryFile, serverLocation).VhConfigureAwait();
            }

            VhLogger.Instance.LogInformation("ServerLocation: {ServerLocation}", serverLocation ?? "Unknown");
            return serverLocation;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning(ex, "Could not read server location.");
            return null;
        }
    }
    private static string? TryToReadFile(string filePath)
    {
        try
        {
            return System.IO.File.Exists(filePath) ? System.IO.File.ReadAllText(filePath) : null;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning(ex, "Could not read file: {FilePath}", filePath);
            return null;
        }
    }

    public virtual Task<ServerCommand> Server_UpdateStatus(ServerStatus serverStatus)
    {
        ServerStatus = serverStatus;
        return Task.FromResult(new ServerCommand(ServerConfig.ConfigCode));
    }

    public virtual Task<ServerConfig> Server_Configure(ServerInfo serverInfo)
    {
        ServerInfo = serverInfo;
        ServerStatus = serverInfo.Status;

        // update UdpEndPoints if they are not configured 
        var udpEndPoints = ServerConfig.UdpEndPointsValue.ToArray();
        foreach (var udpEndPoint in udpEndPoints.Where(x => x.Port == 0))
        {
            udpEndPoint.Port = udpEndPoint.AddressFamily == AddressFamily.InterNetworkV6
                ? serverInfo.FreeUdpPortV6 : serverInfo.FreeUdpPortV4;
        }
        ServerConfig.UdpEndPoints = udpEndPoints.Where(x => x.Port != 0).ToArray();

        return Task.FromResult((ServerConfig)ServerConfig);
    }

    public virtual async Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx)
    {
        var accessItem = await AccessItem_Read(sessionRequestEx.TokenId).VhConfigureAwait();
        if (accessItem == null)
            return new SessionResponseEx
            {
                ErrorCode = SessionErrorCode.AccessError,
                ErrorMessage = "Token does not exist."
            };

        var ret = SessionController.CreateSession(sessionRequestEx, accessItem);
        ret.ServerLocation = _serverToken.ServerLocations?.FirstOrDefault();

        // update accesskey
        if (ServerConfig.ReplyAccessKey)
            ret.AccessKey = accessItem.Token.ToAccessKey();

        return ret;
    }

    public virtual async Task<SessionResponseEx> Session_Get(ulong sessionId, IPEndPoint hostEndPoint, IPAddress? clientIp)
    {
        _ = hostEndPoint;
        _ = clientIp;

        // find token
        var tokenId = SessionController.TokenIdFromSessionId(sessionId);
        if (tokenId == null)
            return new SessionResponseEx
            {
                ErrorCode = SessionErrorCode.AccessError,
                SessionId = sessionId,
                ErrorMessage = "Session does not exist."
            };

        // read accessItem
        var accessItem = await AccessItem_Read(tokenId).VhConfigureAwait();
        if (accessItem == null)
            return new SessionResponseEx
            {
                ErrorCode = SessionErrorCode.AccessError,
                SessionId = sessionId,
                ErrorMessage = "Token does not exist."
            };

        // read usage
        return SessionController.GetSession(sessionId, accessItem, hostEndPoint);
    }

    public virtual Task<SessionResponse> Session_AddUsage(ulong sessionId, Traffic traffic, string? adData)
    {
        return Session_AddUsage(sessionId, traffic, adData, false);
    }

    public virtual Task<SessionResponse> Session_Close(ulong sessionId, Traffic traffic)
    {
        return Session_AddUsage(sessionId, traffic, adData: null, closeSession: true);
    }

    protected virtual bool IsValidAd(string? adData)
    {
        return true; // this server does not validate ad at server side
    }

    private async Task<SessionResponse> Session_AddUsage(ulong sessionId, Traffic traffic, string? adData,
        bool closeSession)
    {

        // find token
        var tokenId = SessionController.TokenIdFromSessionId(sessionId);
        if (tokenId == null)
            return new SessionResponse
            {
                ErrorCode = SessionErrorCode.AccessError,
                ErrorMessage = "Token does not exist."
            };

        // read accessItem
        var accessItem = await AccessItem_Read(tokenId).VhConfigureAwait();
        if (accessItem == null)
            return new SessionResponse
            {
                ErrorCode = SessionErrorCode.AccessError,
                ErrorMessage = "Token does not exist."
            };

        accessItem.AccessUsage.Traffic += traffic;
        await WriteAccessItemUsage(accessItem).VhConfigureAwait();

        if (closeSession)
            SessionController.CloseSession(sessionId);

        // manage adData for simulation
        if (IsValidAd(adData))
            SessionController.Sessions[sessionId].ExpirationTime = null;

        var res = SessionController.GetSession(sessionId, accessItem, null);
        var ret = new SessionResponse
        {
            ErrorCode = res.ErrorCode,
            AccessUsage = res.AccessUsage,
            ErrorMessage = res.ErrorMessage,
            SuppressedBy = res.SuppressedBy
        };

        return ret;
    }

    public virtual void Dispose()
    {
        SessionController.Dispose();
    }

    private string GetAccessItemFileName(string tokenId)
    {
        return Path.Combine(StoragePath, tokenId + FileExtToken);
    }

    private string GetUsageFileName(string tokenId)
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
        System.IO.File.WriteAllBytes(certFilePath, buf);
        return new X509Certificate2(certFilePath, password, X509KeyStorageFlags.Exportable);
    }

    public async Task<AccessItem[]> AccessItem_LoadAll()
    {
        var files = Directory.GetFiles(StoragePath, "*" + FileExtToken);
        var accessItems = new List<AccessItem>();

        foreach (var file in files)
        {
            var accessItem = await AccessItem_Read(Path.GetFileNameWithoutExtension(file)).VhConfigureAwait();
            if (accessItem != null)
                accessItems.Add(accessItem);
        }

        return accessItems.ToArray();
    }

    public int AccessItem_Count()
    {
        var files = Directory.GetFiles(StoragePath, "*" + FileExtToken);
        return files.Length;
    }

    public AccessItem AccessItem_Create(
        int maxClientCount = 1,
        string? tokenName = null,
        int maxTrafficByteCount = 0,
        DateTime? expirationTime = null,
        AdRequirement adRequirement = AdRequirement.None)
    {
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
            AdRequirement = adRequirement,
            Token = new Token
            {
                IssuedAt = DateTime.UtcNow,
                TokenId = Guid.NewGuid().ToString(),
                Secret = aes.Key,
                Name = tokenName,
                SupportId = null,
                ServerToken = _serverToken
            }
        };

        var token = accessItem.Token;

        // Write accessItem without server-token
        var accessItemClone = VhUtil.JsonClone<AccessItem>(accessItem);
        accessItemClone.Token.ServerToken = null!; // remove server token part
        System.IO.File.WriteAllText(GetAccessItemFileName(token.TokenId), JsonSerializer.Serialize(accessItemClone));

        // build default usage
        ReadAccessItemUsage(accessItem).Wait();
        WriteAccessItemUsage(accessItem).Wait();

        return accessItem;
    }

    public async Task AccessItem_Delete(string tokenId)
    {
        // remove index
        _ = await AccessItem_Read(tokenId).VhConfigureAwait()
            ?? throw new KeyNotFoundException("Could not find tokenId");

        // delete files
        if (System.IO.File.Exists(GetUsageFileName(tokenId)))
            System.IO.File.Delete(GetUsageFileName(tokenId));
        if (System.IO.File.Exists(GetAccessItemFileName(tokenId)))
            System.IO.File.Delete(GetAccessItemFileName(tokenId));
    }

    public async Task<AccessItem?> AccessItem_Read(string tokenId)
    {
        // read access item
        var fileName = GetAccessItemFileName(tokenId);
        using var fileLock = await AsyncLock.LockAsync(fileName).VhConfigureAwait();
        if (!System.IO.File.Exists(fileName))
            return null;

        var json = await System.IO.File.ReadAllTextAsync(fileName).VhConfigureAwait();
        var accessItem = VhUtil.JsonDeserialize<AccessItem>(json);
        accessItem.Token.ServerToken = _serverToken; // update server token
        await ReadAccessItemUsage(accessItem).VhConfigureAwait();
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
            using var fileLock = await AsyncLock.LockAsync(fileName).VhConfigureAwait();
            if (System.IO.File.Exists(fileName))
            {
                var json = await System.IO.File.ReadAllTextAsync(fileName).VhConfigureAwait();
                var accessItemUsage = JsonSerializer.Deserialize<AccessItemUsage>(json) ?? new AccessItemUsage();
                accessItem.AccessUsage.Traffic = new Traffic { Sent = accessItemUsage.SentTraffic, Received = accessItemUsage.ReceivedTraffic };
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
            ReceivedTraffic = accessItem.AccessUsage.Traffic.Received,
            SentTraffic = accessItem.AccessUsage.Traffic.Sent
        };
        var json = JsonSerializer.Serialize(accessItemUsage);

        // write accessItem
        var fileName = GetUsageFileName(accessItem.Token.TokenId);
        using var fileLock = await AsyncLock.LockAsync(fileName).VhConfigureAwait();
        await System.IO.File.WriteAllTextAsync(fileName, json).VhConfigureAwait();
    }

    public class AccessItem
    {
        public DateTime? ExpirationTime { get; set; }
        public int MaxClientCount { get; set; }
        public long MaxTraffic { get; set; }
        public AdRequirement AdRequirement { get; set; }
        public required Token Token { get; set; }

        [JsonIgnore]
        public AccessUsage AccessUsage { get; set; } = new();
    }

    private class AccessItemUsage
    {
        public long SentTraffic { get; init; }
        public long ReceivedTraffic { get; init; }
    }
}