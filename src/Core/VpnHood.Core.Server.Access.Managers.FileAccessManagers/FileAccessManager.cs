using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Net.IpLocations;
using VpnHood.Net.IpLocations.Providers.Onlines;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers.Dtos;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers.Services;
using VpnHood.Core.Server.Access.Messaging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Server.Access.Managers.FileAccessManagers;

public class FileAccessManager : IAccessManager
{
    private const string ServerLocationFileName = "server_location";
    private ServerToken _serverToken;
    public FileAccessManagerOptions ServerConfig { get; }
    public string StoragePath { get; }
    public SessionService SessionService { get; }
    public string CertsFolderPath => GetCertsFolderPath(StoragePath);
    public string SessionsFolderPath => GetSessionsFolderPath(StoragePath);
    public X509Certificate2 DefaultCert { get; }
    public ServerStatus? ServerStatus { get; private set; }
    public ServerInfo? ServerInfo { get; private set; }
    public AccessTokenService AccessTokenService { get; }

    // A manager over its storage folder, whose files are read here, as a constructor can't wait on them. The server
    // token carries the server's location: the network is asked for it once, when the settings allow, and the token
    // goes without it otherwise.
    public static async Task<FileAccessManager> Create(string storagePath, FileAccessManagerOptions options,
        CancellationToken cancellationToken)
    {
        var storage = await LoadStorage(storagePath, options, cancellationToken).Vhc();
        return new FileAccessManager(storage);
    }

    // a derived manager's Create reads the storage here; the server secret read or made goes into the options
    protected static async Task<FileAccessManagerStorage> LoadStorage(string storagePath,
        FileAccessManagerOptions options, CancellationToken cancellationToken)
    {
        using var scope = VhLogger.Instance.BeginScope("FileAccessManager");
        ArgumentNullException.ThrowIfNull(storagePath);

        await FindServerLocation(storagePath, options, cancellationToken).Vhc();
        Directory.CreateDirectory(storagePath);
        var defaultCert = await LoadDefaultCert(GetCertsFolderPath(storagePath),
            options.SslCertificatesPassword ?? string.Empty, cancellationToken).Vhc();

        // the certificate is closed when a later step fails, a cancel included
        try {
            options.ServerSecret ??= await LoadServerSecret(GetCertsFolderPath(storagePath), cancellationToken).Vhc();
            var serverToken = await GetAndUpdateServerToken(storagePath, options, defaultCert, cancellationToken).Vhc();
            var sessionService = await SessionService.Create(GetSessionsFolderPath(storagePath), options.IsUnitTest,
                cancellationToken).Vhc();

            return new FileAccessManagerStorage {
                StoragePath = storagePath,
                Options = options,
                DefaultCert = defaultCert,
                ServerToken = serverToken,
                SessionService = sessionService
            };
        }
        catch {
            defaultCert.Dispose();
            throw;
        }
    }

    protected FileAccessManager(FileAccessManagerStorage storage)
    {
        StoragePath = storage.StoragePath;
        ServerConfig = storage.Options;
        AccessTokenService = new AccessTokenService(StoragePath);
        SessionService = storage.SessionService;
        DefaultCert = storage.DefaultCert;
        _serverToken = storage.ServerToken;

        ServerConfig.Certificates = [
            new CertificateData {
                CommonName = DefaultCert.GetNameInfo(X509NameType.DnsName, false) ??
                             throw new Exception("Could not get the HostName from the certificate."),
                RawData = DefaultCert.Export(X509ContentType.Pfx)
            }
        ];

        // UDP is on unless the settings set its endpoints: on TCP's addresses, since a UDP socket on any address answers
        // from the machine's main one, not always the one an app reached; on the port this server keeps (port 0)
        ServerConfig.UdpEndPoints ??= ServerConfig.TcpEndPointsValue
            .Select(x => new IPEndPoint(x.Address, 0))
            .Distinct()
            .ToArray();
    }

    private static string GetCertsFolderPath(string storagePath) => Path.Combine(storagePath, "certificates");
    private static string GetSessionsFolderPath(string storagePath) => Path.Combine(storagePath, "sessions");

    public async Task ClearCache(CancellationToken cancellationToken)
    {
        _serverToken = await GetAndUpdateServerToken(StoragePath, ServerConfig, DefaultCert, cancellationToken).Vhc();
    }

    private static async Task<ServerToken> GetAndUpdateServerToken(string storagePath,
        FileAccessManagerOptions serverConfig, X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        // PublicEndPoints
        var publicEndPoints = serverConfig.PublicEndPoints ?? serverConfig.TcpEndPointsValue;
        if (!publicEndPoints.Any() ||
            publicEndPoints.Any(x => x.Address.Equals(IPAddress.Any) || x.Address.Equals(IPAddress.IPv6Any)))
            throw new Exception("PublicEndPoints has not been configured properly.");


        var serverLocationFile = Path.Combine(storagePath, ServerLocationFileName);
        var serverLocation = await TryToReadFile(serverLocationFile, cancellationToken).Vhc();
        VhLogger.Instance.LogInformation("ServerLocation: {ServerLocation}", serverLocation ?? "Unknown");
        var serverToken = new ServerToken {
            CertificateHash = serverConfig.IsValidHostName ? null : certificate.GetCertHash(),
            HostPort = serverConfig.HostPort ?? publicEndPoints.FirstOrDefault()?.Port ?? 443,
            HostEndPoints = publicEndPoints,
            HostName = certificate.GetNameInfo(X509NameType.DnsName, false) ??
                       throw new Exception("Certificate must have a subject!"),
            IsValidHostName = serverConfig.IsValidHostName,
            Secret = serverConfig.ServerSecret,
            Urls = serverConfig.ServerTokenUrls,
            CreatedTime = VhUtils.RemoveMilliseconds(DateTime.UtcNow),
            ServerLocations = string.IsNullOrEmpty(serverLocation) ? null : [serverLocation]
        };

        // write encrypted server token
        var encServerTokenFilePath = Path.Combine(storagePath, "server-token", "enc-server-token");
        if (File.Exists(encServerTokenFilePath))
            try {
                var oldServerToken = ServerToken.Decrypt(serverConfig.ServerSecret ?? new byte[16],
                    await File.ReadAllTextAsync(encServerTokenFilePath, cancellationToken).Vhc());
                if (!oldServerToken.IsTokenUpdated(serverToken))
                    return oldServerToken;
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogWarning(ex, "Error in reading enc-server-token.");
            }

        Directory.CreateDirectory(Path.GetDirectoryName(encServerTokenFilePath)!);
        await FileUtils.WriteAllTextAtomicAsync(encServerTokenFilePath, serverToken.Encrypt(), cancellationToken).Vhc();
        return serverToken;
    }

    private static async Task<X509Certificate2> LoadDefaultCert(string certsFolderPath, string password,
        CancellationToken cancellationToken)
    {
        var defaultCertFile = Path.Combine(certsFolderPath, "default.pfx");
        if (!File.Exists(defaultCertFile))
            return await CreateSelfSignedCertificate(defaultCertFile, password, cancellationToken).Vhc();

        var buf = await File.ReadAllBytesAsync(defaultCertFile, cancellationToken).Vhc();
        return X509CertificateLoader.LoadPkcs12(buf, password, X509KeyStorageFlags.Exportable);
    }

    private static async Task<byte[]> LoadServerSecret(string certsFolderPath, CancellationToken cancellationToken)
    {
        var serverSecretFile = Path.Combine(certsFolderPath, "secret");
        var secretBase64 = await TryToReadFile(serverSecretFile, cancellationToken).Vhc();
        if (string.IsNullOrEmpty(secretBase64)) {
            secretBase64 = Convert.ToBase64String(VhUtils.GenerateKey(128));
            await FileUtils.WriteAllTextAtomicAsync(serverSecretFile, secretBase64, cancellationToken).Vhc();
        }

        return Convert.FromBase64String(secretBase64);
    }

    // the server's location from the network, once: kept in the storage, where the server token reads it
    private static async Task FindServerLocation(string storagePath, FileAccessManagerOptions options,
        CancellationToken cancellationToken)
    {
        var serverLocationFile = Path.Combine(storagePath, ServerLocationFileName);
        if (!options.UseExternalLocationService ||
            !string.IsNullOrEmpty(await TryToReadFile(serverLocationFile, cancellationToken).Vhc()))
            return;

        try {
            // a limit of its own: the token goes without a location rather than the start waiting for one
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            using var httpClient = new HttpClient();
            const string userAgent = "VpnHood-File-AccessManager";
            var ipLocationProvider = new CompositeCurrentIpLocationProvider(VhLogger.Instance,
            [
                new CloudflareLocationProvider(httpClient, userAgent),
                new IpLocationIoProvider(httpClient, userAgent, apiKey: null),
                new IpApiCoLocationProvider(httpClient, userAgent)
            ]);

            var ipLocation = await ipLocationProvider.GetCurrentLocation(timeoutCts.Token).Vhc();
            var serverLocation = IpLocationProviderFactory.GetPath(ipLocation.CountryCode, ipLocation.RegionName,
                ipLocation.CityName);

            Directory.CreateDirectory(storagePath);
            await FileUtils.WriteAllTextAtomicAsync(serverLocationFile, serverLocation, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not find the server location.");
        }
    }

    // a cancel goes out: taken for a missing file, it would have the secret made anew
    private static async Task<string?> TryToReadFile(string filePath, CancellationToken cancellationToken)
    {
        try {
            return File.Exists(filePath) ? await File.ReadAllTextAsync(filePath, cancellationToken).Vhc() : null;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not read file: {FilePath}", filePath);
            return null;
        }
    }

    public virtual async Task<ServerCommand> Server_UpdateStatus(ServerStatus serverStatus, CancellationToken cancellationToken)
    {
        ServerStatus = serverStatus;
        var result = new ServerCommand(ServerConfig.ConfigCode) {
            SessionResponses = await Session_AddUsages(serverStatus.SessionUsages, cancellationToken).Vhc()
        };
        return result;
    }

    public virtual async Task<ServerConfig> Server_Configure(ServerInfo serverInfo, CancellationToken cancellationToken)
    {
        ServerInfo = serverInfo;
        ServerStatus = serverInfo.Status;

        // port 0 takes the UDP port this server keeps, so a firewall rule made for it holds across restarts
        var udpEndPoints = ServerConfig.UdpEndPointsValue.ToArray();
        if (udpEndPoints.Any(x => x.Port == 0)) {
            var udpPort = await GetUdpPort(serverInfo, cancellationToken).Vhc();
            foreach (var udpEndPoint in udpEndPoints.Where(x => x.Port == 0))
                udpEndPoint.Port = udpPort;
        }
        ServerConfig.UdpEndPoints = udpEndPoints.Where(x => x.Port != 0).ToArray();

        // update QuicEndPoints if they are not configured (port 0 means auto-assign)
        var quicEndPoints = ServerConfig.QuicEndPointsValue.ToArray();
        foreach (var quicEndPoint in quicEndPoints.Where(x => x.Port == 0)) {
            quicEndPoint.Port = quicEndPoint.AddressFamily == AddressFamily.InterNetworkV6
                ? serverInfo.FreeQuicPortV6
                : serverInfo.FreeQuicPortV4;
        }
        ServerConfig.QuicEndPoints = quicEndPoints.Where(x => x.Port != 0).ToArray();

        return ServerConfig;
    }

    // a free port the server found at its first start, one for both IP versions; 0 when it found none
    private async Task<int> GetUdpPort(ServerInfo serverInfo, CancellationToken cancellationToken)
    {
        var udpPortFile = Path.Combine(StoragePath, "udp_port");
        var udpPortText = await TryToReadFile(udpPortFile, cancellationToken).Vhc();
        if (!int.TryParse(udpPortText, out var udpPort) || udpPort is <= 0 or > ushort.MaxValue) {
            udpPort = serverInfo.FreeUdpPortV4 != 0 ? serverInfo.FreeUdpPortV4 : serverInfo.FreeUdpPortV6;
            if (udpPort == 0)
                return 0;

            await FileUtils.WriteAllTextAtomicAsync(udpPortFile, udpPort.ToString(), cancellationToken).Vhc();
        }

        VhLogger.Instance.LogInformation("UDP port: {UdpPort}, kept in {UdpPortFile}. Open it for UDP in the firewall.",
            udpPort, udpPortFile);
        return udpPort;
    }

    public virtual Task<string> Acme_GetHttp01KeyAuthorization(string token, CancellationToken cancellationToken)
    {
        throw new KeyNotFoundException("This server does not support HTTP-01 challenge.");
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
            VhUtils.EncryptClientId(sessionRequestEx.ClientInfo.ClientId, accessTokenData.AccessToken.Secret);
        return encryptClientId.SequenceEqual(sessionRequestEx.EncryptedClientId);
    }

    public virtual async Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx, CancellationToken cancellationToken)
    {
        // validate token
        var accessTokenDataOrg = await AccessTokenService.Find(sessionRequestEx.TokenId, cancellationToken).Vhc();
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
                ? await AccessTokenService.Find(accessTokenId, cancellationToken).Vhc()
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

        // Update usage such as LastUsedTime
        await AccessTokenService.AddUsage(accessTokenData.AccessToken.TokenId, new Traffic(), cancellationToken).Vhc();

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
        IPAddress? clientIp, CancellationToken cancellationToken)
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

        try {
            // read accessItem
            var accessTokenData = await AccessTokenService.Get(tokenId, cancellationToken).Vhc();

            // the server recovers a session for its client's request: a use
            SessionService.UpdateLastUsedTime(sessionId);

            // read usage
            return SessionService.GetSessionResponse(sessionId, accessTokenData, hostEndPoint);
        }
        catch (KeyNotFoundException) {
            return new SessionResponseEx {
                ErrorCode = SessionErrorCode.AccessError,
                SessionId = sessionId,
                ErrorMessage = "Token does not exist."
            };
        }
    }

    public async Task<SessionResponseEx[]> Session_GetAll(CancellationToken cancellationToken)
    {
        // read all sessions
        var responses = new List<SessionResponseEx>();
        foreach (var session in SessionService.Sessions) {
            try {
                // read accessItem
                var accessTokenData = await AccessTokenService.Find(session.Value.TokenId, cancellationToken).Vhc();
                if (accessTokenData != null)
                    responses.Add(SessionService.GetSessionResponse(session.Key, accessTokenData,
                        session.Value.HostEndPoint));
            }
            // a cancellation is no failure of this session: it passes
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception e) {
                VhLogger.Instance.LogError(e, "Failed to get session. SessionId: {SessionId}", session.Key);
            }
        }

        return [.. responses];
    }


    public Task<SessionResponse> Session_AddUsage(ulong sessionId, Traffic traffic, string? adData, CancellationToken cancellationToken)
    {
        return Session_AddUsage(new SessionUsage {
            SessionId = sessionId,
            Sent = traffic.Sent,
            Received = traffic.Received,
            AdData = adData
        }, cancellationToken);
    }

    public virtual Task<SessionResponse> Session_Close(ulong sessionId, Traffic traffic, CancellationToken cancellationToken)
    {
        return Session_AddUsage(new SessionUsage {
            SessionId = sessionId,
            Sent = traffic.Sent,
            Received = traffic.Received,
            ErrorCode = SessionErrorCode.SessionClosed
        }, cancellationToken);
    }

    public async Task<Dictionary<ulong, SessionResponse>> Session_AddUsages(SessionUsage[] sessionUsages, CancellationToken cancellationToken)
    {
        var ret = new Dictionary<ulong, SessionResponse>();
        foreach (var sessionUsage in sessionUsages) {
            try {
                var sessionResponse = await Session_AddUsage(sessionUsage, cancellationToken).Vhc();
                ret[sessionUsage.SessionId] = sessionResponse;
            }
            // a cancellation passes rather than becoming the session's AccessError
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
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
                var sessionResponse = await Session_AddUsage(sessionUsage, cancellationToken).Vhc();
                ret[sessionUsage.SessionId] = sessionResponse;
            }
            // a cancellation passes rather than becoming the session's AccessError
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
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

    protected virtual async Task<SessionResponse> Session_AddUsage(SessionUsage sessionUsage, 
        CancellationToken cancellationToken)
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
        var accessTokenData = await AccessTokenService.Find(tokenId, cancellationToken).Vhc();
        if (accessTokenData == null)
            return new SessionResponse {
                ErrorCode = SessionErrorCode.AccessError,
                ErrorMessage = "Token does not exist."
            };

        var traffic = sessionUsage.ToTraffic();
        await AccessTokenService.AddUsage(tokenId, traffic, cancellationToken).Vhc();

        // a session's traffic is its use
        if (traffic.Total > 0)
            SessionService.UpdateLastUsedTime(sessionId);

        if (sessionUsage.ErrorCode != SessionErrorCode.Ok)
            SessionService.CloseSession(sessionId, sessionUsage.ErrorCode);

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


    public string GetCertFilePath(IPEndPoint ipEndPoint)
    {
        return Path.Combine(CertsFolderPath, ipEndPoint.ToString().Replace(":", "-") + ".pfx");
    }

    private static async Task<X509Certificate2> CreateSelfSignedCertificate(string certFilePath, string password,
        CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation($"Creating Certificate file: {certFilePath}");
        var certificate = CertificateUtil.CreateSelfSigned();
        var buf = certificate.Export(X509ContentType.Pfx, password);
        Directory.CreateDirectory(Path.GetDirectoryName(certFilePath)!);
        await FileUtils.WriteAllBytesAtomicAsync(certFilePath, buf, cancellationToken).Vhc();
        return X509CertificateLoader.LoadPkcs12(buf, password, X509KeyStorageFlags.Exportable);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing) {
            SessionService.Dispose();
            DefaultCert.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}