using VpnHood.AppLib.Abstractions.Ads;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.WebHost.Helpers;
using VpnHood.AppLib.Contracts.Settings;
using VpnHood.AppLib.Contracts.SplitTunneling;
using VpnHood.Core.Common.Tokens;
using WatsonWebserver.Core;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.Api.WebHost.Controllers;

internal class AppController(IAppApi api) : ControllerBase
{
    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/app/";

        // Who asked is a property of the connection, so only this transport can answer it; in
        // process there is no request and the UI is the device's own.
        mapper.AddStatic(HttpMethod.PATCH, baseUrl + "configure", async ctx => {
            var body = ctx.ReadJson<ConfigParams>();
            var res = await api.Configure(body, ctx.Token);
            res.IsRemote = ctx.IsRemote();
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "info", async ctx => {
            var res = await api.GetInfo(ctx.Token);
            res.IsRemote = ctx.IsRemote();
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "split-by-ips-via-app", async ctx => {
            var res = await api.GetSplitIpsViaApp(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.PUT, baseUrl + "split-by-ips-via-app", async ctx => {
            var body = ctx.ReadJson<SplitIpsViaApp>();
            await api.SetSplitIpsViaApp(body, ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "split-by-ips-via-device", async ctx => {
            var res = await api.GetSplitIpsViaDevice(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.PUT, baseUrl + "split-by-ips-via-device", async ctx => {
            var body = ctx.ReadJson<SplitIpsViaDevice>();
            await api.SetSplitIpsViaDevice(body, ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "split-by-domains", async ctx => {
            var res = await api.GetSplitDomains(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.PUT, baseUrl + "split-by-domains", async ctx => {
            var body = ctx.ReadJson<SplitDomains>();
            await api.SetSplitDomains(body, ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "state", async ctx => {
            var res = await api.GetState(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "connect", async ctx => {
            await api.Connect(
                ctx.GetQueryParameter<Guid?>("clientProfileId", null),
                ctx.GetQueryParameter<string?>("serverLocation", null),
                ctx.GetQueryParameter("planId", ConnectPlanId.Normal),
                ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "diagnose", async ctx => {
            await api.Diagnose(
                ctx.GetQueryParameter<Guid?>("clientProfileId", null),
                ctx.GetQueryParameter<string?>("serverLocation", null),
                ctx.GetQueryParameter("planId", ConnectPlanId.Normal),
                ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "disconnect", async ctx => {
            await api.Disconnect(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "version-check", async ctx => {
            await api.VersionCheck(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "version-check-postpone", async ctx => {
            await api.VersionCheckPostpone(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "clear-last-error", async ctx => {
            await api.ClearLastError(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "clear-reconnect-required", async ctx => {
            await api.ClearReconnectRequired(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "extend-by-rewarded-ad", async ctx => {
            await api.ExtendByRewardedAd(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.PUT, baseUrl + "user-settings", async ctx => {
            var body = ctx.ReadJson<UserSettings>();
            await api.SetUserSettings(body, ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "log.txt", async ctx => {
            var text = await api.Log(ctx.Token);
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.Send(text);
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "promotion.jpg", async ctx => {
            var imageBytes = await api.PromotionImage(ctx.Token);
            ctx.Response.ContentType = "image/jpeg";
            await ctx.Response.Send(imageBytes);
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "installed-apps", async ctx => {
            var res = await api.GetInstalledApps(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "user-review", async ctx => {
            var body = ctx.ReadJson<AppUserReview>();
            await api.SetUserReview(body, ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "internal-ad/dismiss", async ctx => {
            var s = ctx.GetQueryParameter<string?>("result");
            var ok = Enum.TryParse<ShowAdResult>(s, true, out var result);
            await api.InternalAdDismiss(ok ? result : default, ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "internal-ad/error", async ctx => {
            await api.InternalAdError(ctx.GetQueryParameter<string>("errorMessage"), ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "countries", async ctx => {
            var res = await api.GetCountries(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "supported-split-by-countries", async ctx => {
            var res = await api.GetSupportedSplitCountries(ctx.Token);
            await ctx.SendJson(res);
        });

        // Pairing is the device's own business: a phone that reached the app through it can
        // neither read, end nor start it, whatever the SPA it runs shows.
        mapper.AddStatic(HttpMethod.GET, baseUrl + "remote-access", async ctx => {
            RequireLocal(ctx);
            var res = await api.GetRemoteAccess(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "remote-access/start", async ctx => {
            RequireLocal(ctx);
            var res = await api.StartRemoteAccess(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "remote-access/stop", async ctx => {
            RequireLocal(ctx);
            await api.StopRemoteAccess(ctx.Token);
            await ctx.SendNoContent();
        });
    }

    // Mapped to 403 by the route mapper.
    private static void RequireLocal(HttpContextBase ctx)
    {
        if (ctx.IsRemote())
            throw new UnauthorizedAccessException("Remote access is controlled from the device itself.");
    }
}
