using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using VpnHood.Common.IpLocations;
using VpnHood.Common.IpLocations.Providers;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Tokens;
using VpnHood.Common.Utils;
using VpnHood.Server.Access.Configurations;
using VpnHood.Server.Access.Managers.FileAccessManagers.Dtos;
using VpnHood.Server.Access.Managers.FileAccessManagers.Services;
using VpnHood.Server.Access.Messaging;

namespace VpnHood.Server.Access.Managers.FileAccessManagers;

public class FileAccessManager : IAccessManager
{
    private ServerToken _serverToken;
    public FileAccessManagerOptions ServerConfig { get; }
    public string StoragePath { get; }
    public SessionService SessionService { get; }
    public string CertsFolderPath => Path.Combine(StoragePath, "certificates");
    public string SessionsFolderPath => Path.Combine(StoragePath, "sessions");
    public X509Certificate2 DefaultCert { get; }
    public ServerStatus? ServerStatus { get; private set; }
    public ServerInfo? ServerInfo { get; private set; }
    public bool IsMaintenanceMode => false; //this server never goes into maintenance mode
    public AccessTokenService AccessTokenService { get; }

    public FileAccessManager(string storagePath, FileAccessManagerOptions options)
    {
        using var scope = VhLogger.Instance.BeginScope("FileAccessManager");

        StoragePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
        ServerConfig = options;
        SessionService = new SessionService(SessionsFolderPath);
        Directory.CreateDirectory(StoragePath);

        var defaultCertFile = Path.Combine(CertsFolderPath, "default.pfx");
        DefaultCert = File.Exists(defaultCertFile)
            ? new X509Certificate2(defaultCertFile, options.SslCertificatesPassword ?? string.Empty, X509KeyStorageFlags.Exportable)
            : CreateSelfSignedCertificate(defaultCertFile, options.SslCertificatesPassword ?? string.Empty);

        ServerConfig.Certificates = [
            new CertificateData {
                CommonName = DefaultCert.GetNameInfo(X509NameType.DnsName, false) ??
                             throw new Exception("Could not get the HostName from the certificate."),
                RawData = DefaultCert.Export(X509ContentType.Pfx)
            }
        ];

        // get or create server secret
        ServerConfig.ServerSecret ??= LoadServerSecret();

        // get server token
        _serverToken = GetAndUpdateServerToken();
        AccessTokenService = new AccessTokenService(storagePath);
    }

    public void ClearCache()
    {
        _serverToken = GetAndUpdateServerToken();
    }

    private ServerToken GetAndUpdateServerToken() => GetAndUpdateServerToken(ServerConfig, DefaultCert,
        Path.Combine(StoragePath, "server-token", "enc-server-token"));

    private ServerToken GetAndUpdateServerToken(FileAccessManagerOptions serverConfig, X509Certificate2 certificate,
        string encServerTokenFilePath)
    {
        // PublicEndPoints
        var publicEndPoints = serverConfig.PublicEndPoints ?? serverConfig.TcpEndPointsValue;
        if (!publicEndPoints.Any() ||
            publicEndPoints.Any(x => x.Address.Equals(IPAddress.Any) || x.Address.Equals(IPAddress.IPv6Any)))
            throw new Exception("PublicEndPoints has not been configured properly.");


        var serverLocation = LoadServerLocation().Result;
        var serverToken = new ServerToken {
            CertificateHash = serverConfig.IsValidHostName ? null : certificate.GetCertHash(),
            HostPort = serverConfig.HostPort ?? publicEndPoints.FirstOrDefault()?.Port ?? 443,
            HostEndPoints = publicEndPoints,
            HostName = certificate.GetNameInfo(X509NameType.DnsName, false) ??
                       throw new Exception("Certificate must have a subject!"),
            IsValidHostName = serverConfig.IsValidHostName,
            Secret = serverConfig.ServerSecret,
            Urls = serverConfig.ServerTokenUrls,
            CreatedTime = VhUtil.RemoveMilliseconds(DateTime.UtcNow),
            ServerLocations = string.IsNullOrEmpty(serverLocation) ? null : [serverLocation]
        };

        // write encrypted server token
        if (File.Exists(encServerTokenFilePath))
            try {
                var oldServerToken = ServerToken.Decrypt(serverConfig.ServerSecret ?? new byte[16],
                    File.ReadAllText(encServerTokenFilePath));
                if (!oldServerToken.IsTokenUpdated(serverToken))
                    return oldServerToken;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogWarning(ex, "Error in reading enc-server-token.");
            }

        Directory.CreateDirectory(Path.GetDirectoryName(encServerTokenFilePath)!);
        File.WriteAllText(encServerTokenFilePath, serverToken.Encrypt());
        return serverToken;
    }

    private byte[] LoadServerSecret()
    {
        var serverSecretFile = Path.Combine(CertsFolderPath, "secret");
        var secretBase64 = TryToReadFile(serverSecretFile);
        if (string.IsNullOrEmpty(secretBase64)) {
            secretBase64 = Convert.ToBase64String(VhUtil.GenerateKey(128));
            File.WriteAllText(serverSecretFile, secretBase64);
        }

        return Convert.FromBase64String(secretBase64);
    }

    private async Task<string?> LoadServerLocation()
    {
        try {
            var serverCountryFile = Path.Combine(StoragePath, "server_location");
            var serverLocation = TryToReadFile(serverCountryFile);
            if (string.IsNullOrEmpty(serverLocation) && ServerConfig.UseExternalLocationService) {
                using var cancellationTokenSource = new CancellationTokenSource(5000);
                using var httpClient = new HttpClient();
                const string userAgent = "VpnHood-File-AccessManager";
                var ipLocationProvider = new CompositeIpLocationProvider(VhLogger.Instance,
                [
                    new CloudflareLocationProvider(httpClient, userAgent),
                    new IpLocationIoProvider(httpClient, userAgent, apiKey: null),
                    new IpApiCoLocationProvider(httpClient, userAgent)
                ]);

                var ipLocation = await ipLocationProvider.GetCurrentLocation(cancellationTokenSource.Token).VhConfigureAwait();
                serverLocation = IpLocationProviderFactory.GetPath(ipLocation.CountryCode, ipLocation.RegionName, ipLocation.CityName);
                await File.WriteAllTextAsync(serverCountryFile, serverLocation, CancellationToken.None).VhConfigureAwait();
            }

            VhLogger.Instance.LogInformation("ServerLocation: {ServerLocation}", serverLocation ?? "Unknown");
            return serverLocation;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not read server location.");
            return null;
        }
    }

    private static string? TryToReadFile(string filePath)
    {
        try {
            return File.Exists(filePath) ? File.ReadAllText(filePath) : null;
        }
        catch (Exception ex) {
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
        foreach (var udpEndPoint in udpEndPoints.Where(x => x.Port == 0)) {
            udpEndPoint.Port = udpEndPoint.AddressFamily == AddressFamily.InterNetworkV6
                ? serverInfo.FreeUdpPortV6
                : serverInfo.FreeUdpPortV4;
        }

        ServerConfig.UdpEndPoints = udpEndPoints.Where(x => x.Port != 0).ToArray();

        return Task.FromResult((ServerConfig)ServerConfig);
    }

    public virtual async Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx)
    {
        var accessTokenData = await AccessTokenService.TryGet(sessionRequestEx.TokenId).VhConfigureAwait();
        if (accessTokenData == null)
            return new SessionResponseEx {
                ErrorCode = SessionErrorCode.AccessError,
                ErrorMessage = "Token does not exist."
            };

        var ret = SessionService.CreateSession(sessionRequestEx, accessTokenData);
        var locationInfo = _serverToken.ServerLocations?.Any() == true
            ? ServerLocationInfo.Parse(_serverToken.ServerLocations.First())
            : null;

        ret.ServerLocation = locationInfo?.ServerLocation;

        // update accesskey
        if (ServerConfig.ReplyAccessKey)
            ret.AccessKey = GetToken(accessTokenData.AccessToken).ToAccessKey();

        return ret;
    }

    public Token CreateToken(int maxClientCount = 1, string? tokenName = null, int maxTrafficByteCount = 0, 
        DateTime? expirationTime = null, AdRequirement adRequirement = AdRequirement.None)
    {
        var accessToken = AccessTokenService.Create(
            tokenName: tokenName,
            maxClientCount: maxClientCount,
            maxTrafficByteCount: maxTrafficByteCount,
            expirationTime: expirationTime,
            adRequirement: adRequirement
        );

        return GetToken(accessToken);
    }


    public Token GetToken(AccessToken accessToken)
    {
        return new Token {
            TokenId = accessToken.TokenId,
            Name = accessToken.Name,
            IssuedAt = accessToken.IssuedAt,
            Secret = accessToken.Secret,
            ServerToken = _serverToken,
            SupportId = null
        };
    }

    public virtual async Task<SessionResponseEx> Session_Get(ulong sessionId, IPEndPoint hostEndPoint,
        IPAddress? clientIp)
    {
        _ = hostEndPoint;
        _ = clientIp;

        // find token
        var tokenId = SessionService.TokenIdFromSessionId(sessionId);
        if (tokenId == null)
            return new SessionResponseEx {
                ErrorCode = SessionErrorCode.AccessError,
                SessionId = sessionId,
                ErrorMessage = "Session does not exist."
            };

        // read accessItem
        var accessTokenData = await AccessTokenService.Get(tokenId).VhConfigureAwait();
        if (accessTokenData == null)
            return new SessionResponseEx {
                ErrorCode = SessionErrorCode.AccessError,
                SessionId = sessionId,
                ErrorMessage = "Token does not exist."
            };

        // read usage
        return SessionService.GetSession(sessionId, accessTokenData, hostEndPoint);
    }

    public async Task<SessionResponseEx[]> Session_GetAll()
    {
        // get all tokenIds
        //var tokenIds = SessionService.Sessions.Select(x => x.Value.TokenId);
        //// read all accessItems
        //var accessItems = await Task.WhenAll(tokenIds.Select(AccessItem_Read));

        //return SessionService.GetSessions(accessItems);
        throw new NotImplementedException();
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
        var tokenId = SessionService.TokenIdFromSessionId(sessionId);
        if (tokenId == null)
            return new SessionResponse {
                ErrorCode = SessionErrorCode.AccessError,
                ErrorMessage = "Token does not exist."
            };

        // read accessItem
        var accessTokenData = await AccessTokenService.Get(tokenId).VhConfigureAwait();
        if (accessTokenData == null)
            return new SessionResponse {
                ErrorCode = SessionErrorCode.AccessError,
                ErrorMessage = "Token does not exist."
            };

        await AccessTokenService.AddUsage(tokenId, traffic);

        if (closeSession)
            SessionService.CloseSession(sessionId);

        // manage adData for simulation
        if (IsValidAd(adData))
            SessionService.Sessions[sessionId].ExpirationTime = null;

        var res = SessionService.GetSession(sessionId, accessTokenData, null);
        var ret = new SessionResponse {
            ErrorCode = res.ErrorCode,
            AccessUsage = res.AccessUsage,
            ErrorMessage = res.ErrorMessage,
            SuppressedBy = res.SuppressedBy
        };

        return ret;
    }

    public virtual void Dispose()
    {
        SessionService.Dispose();
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
}