using System.Net;
using VpnHood.Core.Common.Messaging;
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
    public IPEndPoint? RedirectHostEndPoint { get; set; }
    public IPEndPoint[]? RedirectHostEndPoints { get; set; }
    public Dictionary<string, IPEndPoint?> ServerLocations { get; set; } = new();
    public bool RejectAllAds { get; set; }
    public bool CanExtendPremiumByAd { get; set; }
    public Dictionary<string, string> AccessCodes { get; set; } = new();
    public bool IsUserReviewRecommended { get; set; }
    public DateTime? UserReviewTime { get; set; }
    public int? UserReviewRate { get; set; }

    public void AddAdData(string adData)
    {
        if (!RejectAllAds)
            _adsData.TryAdd(adData, new TimeoutItem());
    }

    protected override bool IsValidAd(string? adData)
    {
        return adData != null && _adsData.TryRemove(adData, out _);
    }

    public override async Task<ServerCommand> Server_UpdateStatus(ServerStatus serverStatus)
    {
        var ret = await base.Server_UpdateStatus(serverStatus);
        LastServerStatus = serverStatus;
        return ret;
    }

    public override Task<ServerConfig> Server_Configure(ServerInfo serverInfo)
    {
        LastConfigureTime = DateTime.Now;
        LastServerInfo = serverInfo;
        LastServerStatus = serverInfo.Status;
        return base.Server_Configure(serverInfo);
    }

    public override async Task<SessionResponseEx> Session_Get(ulong sessionId, IPEndPoint hostEndPoint,
        IPAddress? clientIp)
    {
        lock (_lockObject)
            SessionGetCounter++;

        var sessionResponseEx = await base.Session_Get(sessionId, hostEndPoint, clientIp);
        UpdateSessionResponse(sessionResponseEx);
        return sessionResponseEx;
    }

    public override async Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx)
    {
        var ret = await base.Session_Create(sessionRequestEx);
        // update by test provider
        UpdateSessionResponse(ret);

        // update test provider
        UserReviewRate = sessionRequestEx.UserReviewRate;
        UserReviewTime = sessionRequestEx.UserReviewTime;

        if (!sessionRequestEx.AllowRedirect)
            return ret;

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

        // manage region
        if (sessionRequestEx.ServerLocation != null) {
            // check is location is valid
            if (!ServerLocations.TryGetValue(sessionRequestEx.ServerLocation, out var redirectEndPoint)) {
                ret.ErrorCode = SessionErrorCode.NoServerAvailable;
                return ret;
            }

            // just accepted if it is null 
            if (redirectEndPoint == null) {
                sessionRequestEx.ServerLocation = null;
                return ret;
            }

            // check if location is different
            if (!sessionRequestEx.HostEndPoint.Equals(redirectEndPoint)) {
                ret.RedirectHostEndPoint = ServerLocations[sessionRequestEx.ServerLocation];
                ret.ErrorCode = SessionErrorCode.RedirectHost;
            }
        }

        return ret;
    }

    private void UpdateSessionResponse(SessionResponse sessionResponse)
    {
        if (sessionResponse.AccessUsage != null) {
            sessionResponse.AccessUsage.CanExtendByRewardedAd = CanExtendPremiumByAd;
            sessionResponse.AccessUsage.IsUserReviewRecommended = IsUserReviewRecommended;
        }

    }

    protected override async Task<SessionResponse> Session_AddUsage(SessionUsage sessionUsage)
    {
        // update test provider
        var sessionResponse = await base.Session_AddUsage(sessionUsage);

        // update by test provider
        UpdateSessionResponse(sessionResponse);
        return sessionResponse;
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