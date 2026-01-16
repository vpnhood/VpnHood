using System.Net;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server.Access;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Managers.FileAccessManagement;
using VpnHood.Core.Server.Access.Messaging;
using VpnHood.Core.Toolkit.Collections;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Test.AccessManagers;

public class TestAccessManager(string storagePath, FileAccessManagerOptions options)
    : FileAccessManager(storagePath, options)
{
    private readonly TimeoutDictionary<string, TimeoutItem> _adsData = new(TimeSpan.FromMinutes(10));
    private readonly Lock _lockObject = new();

    public int SessionGetCounter { get; private set; }
    public DateTime? LastConfigureTime { get; private set; }
    public ServerInfo? LastServerInfo { get; private set; }
    public ServerStatus? LastServerStatus { get; private set; }
    public Dictionary<string, ServerToken?> ServerLocations { get; set; } = new();
    public bool RejectAllAds { get; set; }
    public bool CanExtendPremiumByAd { get; set; }
    public Dictionary<string, string> AccessCodes { get; set; } = new();
    public int UserReviewRecommended { get; set; }
    public UserReview? UserReview { get; set; }

    [Obsolete("Use RedirectServerTokens")]
    public IPEndPoint? RedirectHostEndPoint { get; set; }

    [Obsolete("Use RedirectServerTokens")]
    public IPEndPoint[]? RedirectHostEndPoints { get; set; }

    public ServerToken[]? RedirectServerTokens { get; set; }
    public string? AcmeHttp01KeyToken { get; set; }
    public string? AcmeHttp01KeyAuthorization { get; set; }

    public void AddAdData(string adData)
    {
        if (!RejectAllAds)
            _adsData.TryAdd(adData, new TimeoutItem());
    }

    protected override bool IsValidAd(string? adData)
    {
        return adData != null && _adsData.TryRemove(adData, out _);
    }

    public override async Task<ServerCommand> Server_UpdateStatus(ServerStatus serverStatus,
        CancellationToken cancellationToken)
    {
        var ret = await base.Server_UpdateStatus(serverStatus, cancellationToken);
        LastServerStatus = serverStatus;
        return ret;
    }

    public override Task<ServerConfig> Server_Configure(ServerInfo serverInfo, CancellationToken cancellationToken)
    {
        LastConfigureTime = DateTime.Now;
        LastServerInfo = serverInfo;
        LastServerStatus = serverInfo.Status;
        return base.Server_Configure(serverInfo, cancellationToken);
    }

    public override async Task<SessionResponseEx> Session_Get(ulong sessionId, IPEndPoint hostEndPoint,
        IPAddress? clientIp, CancellationToken cancellationToken)
    {
        lock (_lockObject)
            SessionGetCounter++;

        var sessionResponseEx = await base.Session_Get(sessionId, hostEndPoint, clientIp, cancellationToken);
        UpdateSessionResponse(sessionResponseEx);
        return sessionResponseEx;
    }

    public override async Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx,
        CancellationToken cancellationToken)
    {
        var ret = await base.Session_Create(sessionRequestEx, cancellationToken);
        // update by test provider
        UpdateSessionResponse(ret);

        // update test provider
        UserReview = sessionRequestEx.UserReview;

        if (!sessionRequestEx.AllowRedirect)
            return ret;

#pragma warning disable CS0618 // Type or member is obsolete
        if (RedirectHostEndPoint != null &&
            !sessionRequestEx.HostEndPoint.Equals(RedirectHostEndPoint)) {
            ret.RedirectHostEndPoint = RedirectHostEndPoint;
            ret.ErrorCode = SessionErrorCode.RedirectHost;
        }

        // manage new redirects
        if (RedirectHostEndPoints != null) {
            ret.RedirectHostEndPoints = RedirectHostEndPoints;
            ret.ErrorCode = SessionErrorCode.RedirectHost;
        }
#pragma warning restore CS0618 // Type or member is obsolete

        if (RedirectServerTokens != null) {
            ret.RedirectServerTokens = RedirectServerTokens;
            ret.ErrorCode = SessionErrorCode.RedirectHost;
        }

        // manage region
        if (sessionRequestEx.ServerLocation != null) {
            // check is location is valid
            if (!ServerLocations.TryGetValue(sessionRequestEx.ServerLocation, out var redirectServerToken)) {
                ret.ErrorCode = SessionErrorCode.NoServerAvailable;
                return ret;
            }

            // just accepted if it is null 
            if (redirectServerToken == null) {
                sessionRequestEx.ServerLocation = null;
                return ret;
            }

            // check if location is different
            if (!sessionRequestEx.HostEndPoint.Equals(redirectServerToken.HostEndPoints!.First())) {
                ret.RedirectServerTokens = [redirectServerToken];
                ret.ErrorCode = SessionErrorCode.RedirectHost;
            }
        }

        return ret;
    }

    private void UpdateSessionResponse(SessionResponse sessionResponse)
    {
        if (sessionResponse.AccessUsage != null) {
            sessionResponse.AccessUsage.CanExtendByRewardedAd = CanExtendPremiumByAd;
            sessionResponse.AccessUsage.UserReviewRecommended = UserReviewRecommended;
        }
    }

    protected override async Task<SessionResponse> Session_AddUsage(SessionUsage sessionUsage,
        CancellationToken cancellationToken)
    {
        // update test provider
        var sessionResponse = await base.Session_AddUsage(sessionUsage, cancellationToken);

        // update by test provider
        UpdateSessionResponse(sessionResponse);
        return sessionResponse;
    }

    public override Task<string> Acme_GetHttp01KeyAuthorization(string token, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (AcmeHttp01KeyToken == token && AcmeHttp01KeyAuthorization != null)
            return Task.FromResult(AcmeHttp01KeyAuthorization);

        throw new HttpRequestException("Token not found", null, HttpStatusCode.NotFound);
    }


    protected override string? GetAccessTokenIdFromAccessCode(string accessCode)
    {
        var validatedAccessCode = AccessCodeUtils.TryValidate(accessCode);
        if (validatedAccessCode == null)
            return null;

        return AccessCodes.GetValueOrDefault(validatedAccessCode);
    }

    protected override void Dispose(bool disposing)
    {
        _adsData.Dispose();
        base.Dispose(disposing);
    }
}