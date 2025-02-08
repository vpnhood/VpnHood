using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.IpLocations;
using VpnHood.Core.Common.IpLocations.Providers;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers.Dtos;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers.Services;
using VpnHood.Core.Server.Access.Messaging;

namespace VpnHood.Core.Server.Access.Managers.FileAccessManagers;

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
        AccessTokenService = new AccessTokenService(storagePath);
        SessionService = new SessionService(SessionsFolderPath, options.IsUnitTest);
        Directory.CreateDirectory(StoragePath);

        var defaultCertFile = Path.Combine(CertsFolderPath, "default.pfx");
        DefaultCert = File.Exists(defaultCertFile)
            ? new X509Certificate2(defaultCertFile, options.SslCertificatesPassword ?? string.Empty,
                X509KeyStorageFlags.Exportable)
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

                var ipLocation = await ipLocationProvider.GetCurrentLocation(cancellationTokenSource.Token)
                    .VhConfigureAwait();
                serverLocation = IpLocationProviderFactory.GetPath(ipLocation.CountryCode, ipLocation.RegionName,
                    ipLocation.CityName);
                await File.WriteAllTextAsync(serverCountryFile, serverLocation, CancellationToken.None)
                    .VhConfigureAwait();
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

    public virtual async Task<ServerCommand> Server_UpdateStatus(ServerStatus serverStatus)
    {
        ServerStatus = serverStatus;
        var result = new ServerCommand(ServerConfig.ConfigCode) {
            SessionResponses = await Session_AddUsages(serverStatus.SessionUsages).VhConfigureAwait()
        };
        return result;
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

    public static bool ValidateRequest(SessionRequestEx sessionRequestEx,
        [NotNullWhen(true)] AccessTokenData? accessTokenData)
    {
        if (accessTokenData == null)
            return false;

        var encryptClientId =
            VhUtil.EncryptClientId(sessionRequestEx.ClientInfo.ClientId, accessTokenData.AccessToken.Secret);
        return encryptClientId.SequenceEqual(sessionRequestEx.EncryptedClientId);
    }

    public virtual async Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx)
    {
        // validate token
        var accessTokenDataOrg = await AccessTokenService.Find(sessionRequestEx.TokenId).VhConfigureAwait();
        if (!ValidateRequest(sessionRequestEx, accessTokenDataOrg))
            return new SessionResponseEx {
                ErrorCode = SessionErrorCode.AccessError,
                ErrorMessage = "Token does not exist."
            };

        // use original accessTokenData
        var accessTokenData = accessTokenDataOrg;

        // find token for AccessCode 
        if (!string.IsNullOrWhiteSpace(sessionRequestEx.AccessCode)) {
            var accessTokenId = GetAccessTokenIdFromAccessCode(sessionRequestEx.AccessCode);
            accessTokenData = accessTokenId != null
                ? await AccessTokenService.Find(accessTokenId).VhConfigureAwait()
                : null;
            if (accessTokenData == null)
                return new SessionResponseEx {
                    ErrorCode = SessionErrorCode.AccessCodeRejected,
                    ErrorMessage = "The given AccessCode has been rejected."
                };
        }

        var ret = SessionService.CreateSession(sessionRequestEx, accessTokenData);
        if (ret.ErrorCode is SessionErrorCode.AccessError or SessionErrorCode.AccessLocked)
            return ret; // not more information should send

        // set server location
        var locationInfo = _serverToken.ServerLocations?.Any() == true
            ? ServerLocationInfo.Parse(_serverToken.ServerLocations.First())
            : null;
        ret.ServerLocation = locationInfo?.ServerLocation;

        // update accesskey
        if (ServerConfig.ReplyAccessKey)
            ret.AccessKey = GetToken(accessTokenDataOrg.AccessToken).ToAccessKey();

        return ret;
    }

    protected virtual string? GetAccessTokenIdFromAccessCode(string accessCode)
    {
        return null;
    }

    public virtual async Task<SessionResponseEx> Session_Get(ulong sessionId, IPEndPoint hostEndPoint,
        IPAddress? clientIp)
    {
        _ = hostEndPoint;
        _ = clientIp;

        // find token
        var tokenId = SessionService.FindTokenIdFromSessionId(sessionId);
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
        return SessionService.GetSessionResponse(sessionId, accessTokenData, hostEndPoint);
    }

    public async Task<SessionResponseEx[]> Session_GetAll()
    {
        // read all sessions
        var responses = new List<SessionResponseEx>();
        foreach (var session in SessionService.Sessions) {
            try {
                // read accessItem
                var accessTokenData = await AccessTokenService.Find(session.Value.TokenId).VhConfigureAwait();
                if (accessTokenData != null)
                    responses.Add(SessionService.GetSessionResponse(session.Key, accessTokenData,
                        session.Value.HostEndPoint));
            }
            catch (Exception e) {
                VhLogger.Instance.LogError(e, "Failed to get session. SessionId: {SessionId}", session.Key);
            }
        }

        return responses.ToArray();
    }


    public virtual Task<SessionResponse> Session_AddUsage(ulong sessionId, Traffic traffic, string? adData)
    {
        return Session_AddUsage(new SessionUsage {
            SessionId = sessionId,
            Sent = traffic.Sent,
            Received = traffic.Received,
            Closed = false,
            AdData = adData
        });
    }

    public virtual Task<SessionResponse> Session_Close(ulong sessionId, Traffic traffic)
    {
        return Session_AddUsage(new SessionUsage {
            SessionId = sessionId,
            Sent = traffic.Sent,
            Received = traffic.Received,
            Closed = true
        });
    }

    public async Task<Dictionary<ulong, SessionResponse>> Session_AddUsages(SessionUsage[] sessionUsages)
    {
        var ret = new Dictionary<ulong, SessionResponse>();
        foreach (var sessionUsage in sessionUsages) {
            try {
                var sessionResponse = await Session_AddUsage(sessionUsage);
                ret[sessionUsage.SessionId] = sessionResponse;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Failed to add usage. SessionId: {SessionId}", sessionUsage.SessionId);
                ret[sessionUsage.SessionId] = new SessionResponse {
                    ErrorCode = SessionErrorCode.AccessError,
                    ErrorMessage = "Failed to add usage."
                };
            }
        }

        // add updated sessions that are not in the list
        var updatedSessionIds = SessionService.ResetUpdatedSessions();
        foreach (var updatedSessionId in updatedSessionIds.Where(x => !ret.ContainsKey(x))) {
            var sessionUsage = new SessionUsage {
                SessionId = updatedSessionId
            };

            try {
                var sessionResponse = await Session_AddUsage(sessionUsage);
                ret[sessionUsage.SessionId] = sessionResponse;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Failed to add usage. SessionId: {SessionId}", sessionUsage.SessionId);
                ret[sessionUsage.SessionId] = new SessionResponse {
                    ErrorCode = SessionErrorCode.AccessError,
                    ErrorMessage = "Failed to add usage."
                };
            }
        }

        return ret;
    }

    private async Task<SessionResponse> Session_AddUsage(SessionUsage sessionUsage)
    {
        var sessionId = sessionUsage.SessionId;

        // find token
        var tokenId = SessionService.FindTokenIdFromSessionId(sessionId);
        if (tokenId == null)
            return new SessionResponse {
                ErrorCode = SessionErrorCode.AccessError,
                ErrorMessage = "Token does not exist."
            };

        // read accessItem
        var accessTokenData = await AccessTokenService.Find(tokenId).VhConfigureAwait();
        if (accessTokenData == null)
            return new SessionResponse {
                ErrorCode = SessionErrorCode.AccessError,
                ErrorMessage = "Token does not exist."
            };

        await AccessTokenService.AddUsage(tokenId, sessionUsage.ToTraffic());

        if (sessionUsage.Closed)
            SessionService.CloseSession(sessionId);

        // manage adData for simulation
        var isValidAd = string.IsNullOrEmpty(sessionUsage.AdData) ? (bool?)null : IsValidAd(sessionUsage.AdData);

        var res = SessionService.GetSessionResponse(sessionId, accessTokenData, null, isValidAd: isValidAd);
        var ret = new SessionResponse {
            ErrorCode = res.ErrorCode,
            AccessUsage = res.AccessUsage,
            ErrorMessage = res.ErrorMessage,
            SuppressedBy = res.SuppressedBy
        };

        return ret;
    }

    protected virtual bool IsValidAd(string? adData)
    {
        return true; // this server does not validate ad at server side
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