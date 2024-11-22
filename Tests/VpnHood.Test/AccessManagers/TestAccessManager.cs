using System.Net;
using VpnHood.Common.Collections;
using VpnHood.Common.Messaging;
using VpnHood.Server.Access;
using VpnHood.Server.Access.Configurations;
using VpnHood.Server.Access.Managers.FileAccessManagers;
using VpnHood.Server.Access.Messaging;

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

    public override async Task<SessionResponseEx> Session_Get(ulong sessionId, IPEndPoint hostEndPoint, IPAddress? clientIp)
    {
        lock (_lockObject)
            SessionGetCounter++;

        var session = await base.Session_Get(sessionId, hostEndPoint, clientIp);

        if (session.AccessUsage != null)
            session.AccessUsage.CanExtendPremiumByAdReward = CanExtendPremiumByAd;

        return session;
    }

    public override async Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx)
    {
        var ret = await base.Session_Create(sessionRequestEx);
        if (ret.AccessUsage != null)
            ret.AccessUsage.CanExtendPremiumByAdReward = CanExtendPremiumByAd;

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

            if (!sessionRequestEx.HostEndPoint.Equals(redirectEndPoint)) {
                ret.RedirectHostEndPoint = ServerLocations[sessionRequestEx.ServerLocation];
                ret.ErrorCode = SessionErrorCode.RedirectHost;
            }
        }

        return ret;
    }
}