using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.WebHost.Helpers;
using VpnHood.AppLib.Api.Ads;
using VpnHood.AppLib.Api.Sessions;
using VpnHood.AppLib.Api.Settings;
using VpnHood.AppLib.Api.SplitTunneling;
using VpnHood.Net.Toolkit.Extensions;
using WatsonWebserver.Core;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.Api.WebHost.Controllers;

internal class AppController(IAppApi api) : ControllerBase
{
    // Under the API, but loopback asks no token for it (VpnHoodAppWebHost): the app downloads it from
    // its server, and it holds nothing secret.
    internal const string PromotionImagePath = "/api/app/promotion.jpg";

    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/app/";

        // Who asked is a property of the connection, so only this transport can answer it; in
        // process there is no request and the UI is the device's own.
        mapper.AddStatic(HttpMethod.PATCH, baseUrl + "configure", async ctx => {
            var body = ctx.ReadJson<ConfigParams>();
            var res = await api.Configure(body, ctx.Token).Vhc();
            res.IsRemote = ctx.IsRemote();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "info", async ctx => {
            var res = await api.GetInfo(ctx.Token).Vhc();
            res.IsRemote = ctx.IsRemote();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "split-by-ips-via-app", async ctx => {
            var res = await api.GetSplitIpsViaApp(ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.PUT, baseUrl + "split-by-ips-via-app", async ctx => {
            var body = ctx.ReadJson<SplitIpsViaApp>();
            await api.SetSplitIpsViaApp(body, ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "split-by-ips-via-device", async ctx => {
            var res = await api.GetSplitIpsViaDevice(ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.PUT, baseUrl + "split-by-ips-via-device", async ctx => {
            var body = ctx.ReadJson<SplitIpsViaDevice>();
            await api.SetSplitIpsViaDevice(body, ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "split-by-domains", async ctx => {
            var res = await api.GetSplitDomains(ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.PUT, baseUrl + "split-by-domains", async ctx => {
            var body = ctx.ReadJson<SplitDomains>();
            await api.SetSplitDomains(body, ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "state", async ctx => {
            var res = await api.GetState(ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "connect", async ctx => {
            await api.Connect(
                ctx.GetQueryParameter<Guid?>("vpnProfileId", null),
                ctx.GetQueryParameter<string?>("serverLocation", null),
                ctx.GetQueryParameter("planId", ConnectPlanId.Normal),
                ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "diagnose", async ctx => {
            await api.Diagnose(
                ctx.GetQueryParameter<Guid?>("vpnProfileId", null),
                ctx.GetQueryParameter<string?>("serverLocation", null),
                ctx.GetQueryParameter("planId", ConnectPlanId.Normal),
                ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "disconnect", async ctx => {
            await api.Disconnect(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "version-check", async ctx => {
            var res = await api.VersionCheck(ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "version-check-postpone", async ctx => {
            await api.VersionCheckPostpone(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "start-update", async ctx => {
            await api.StartUpdate(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "complete-update", async ctx => {
            await api.CompleteUpdate(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "automatic-update", async ctx => {
            var res = await api.GetAutomaticUpdate(ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.PUT, baseUrl + "automatic-update", async ctx => {
            var body = ctx.ReadJson<bool>();
            await api.SetAutomaticUpdate(body, ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "clear-last-error", async ctx => {
            await api.ClearLastError(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "clear-reconnect-required", async ctx => {
            await api.ClearReconnectRequired(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "extend-by-rewarded-ad", async ctx => {
            await api.ExtendByRewardedAd(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "ad-privacy-options", async ctx => {
            await api.ShowAdPrivacyOptions(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.PUT, baseUrl + "user-settings", async ctx => {
            var body = ctx.ReadJson<UserSettings>();
            await api.SetUserSettings(body, ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "log.txt", async ctx => {
            var text = await api.Log(ctx.Token).Vhc();
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            await ctx.Response.Send(text).Vhc();
        });

        mapper.AddStatic(HttpMethod.GET, PromotionImagePath, async ctx => {
            var imageBytes = await api.PromotionImage(ctx.Token).Vhc();
            ctx.Response.ContentType = "image/jpeg";
            await ctx.Response.Send(imageBytes).Vhc();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "installed-apps", async ctx => {
            var res = await api.GetInstalledApps(ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "user-review", async ctx => {
            var body = ctx.ReadJson<AppUserReview>();
            await api.SetUserReview(body, ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "internal-ad/dismiss", async ctx => {
            var s = ctx.GetQueryParameter<string?>("result");
            var ok = Enum.TryParse<ShowAdResult>(s, true, out var result);
            await api.InternalAdDismiss(ok ? result : default, ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "internal-ad/error", async ctx => {
            await api.InternalAdError(ctx.GetQueryParameter<string>("errorMessage"), ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "countries", async ctx => {
            var res = await api.GetCountries(ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "supported-split-by-countries", async ctx => {
            var res = await api.GetSupportedSplitCountries(ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        // Pairing is the device's own business: a phone that reached the app through it can
        // neither read, end nor start it, whatever the page it runs shows.
        mapper.AddStatic(HttpMethod.GET, baseUrl + "remote-access", async ctx => {
            RequireLocal(ctx);
            var res = await api.GetRemoteAccess(ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "remote-access/start", async ctx => {
            RequireLocal(ctx);
            var res = await api.StartRemoteAccess(ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "remote-access/stop", async ctx => {
            RequireLocal(ctx);
            await api.StopRemoteAccess(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });
    }

    // Mapped to 403 by the route mapper.
    private static void RequireLocal(HttpContextBase ctx)
    {
        if (ctx.IsRemote())
            throw new UnauthorizedAccessException("Remote access is controlled from the device itself.");
    }
}
