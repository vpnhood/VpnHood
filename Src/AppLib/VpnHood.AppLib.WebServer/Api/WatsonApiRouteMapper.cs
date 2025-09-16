using System.Net;
using System.Text.Json;
using VpnHood.AppLib.WebServer.Controllers;
using VpnHood.AppLib.ClientProfiles;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer.Api;

internal class WatsonApiRouteMapper
{
    private readonly WebserverLite _server;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public WatsonApiRouteMapper(WebserverLite server)
    {
        _server = server;
        MapRoutes();
    }

    private void Cors(HttpContextBase ctx)
    {
        ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,PATCH,DELETE,OPTIONS");
        ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        ctx.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
    }

    private Task Options(HttpContextBase ctx)
    {
        Cors(ctx);
        ctx.Response.StatusCode = (int)HttpStatusCode.OK;
        return ctx.Response.Send();
    }

    private async Task SendJson(HttpContextBase ctx, object? data, int statusCode = 200)
    {
        Cors(ctx);
        if (data is null)
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.NoContent;
            await ctx.Response.Send();
            return;
        }

        var json = JsonSerializer.Serialize(data, _jsonOptions);
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.Send(json);
    }

    private T ReadJson<T>(HttpContextBase ctx)
    {
        var body = ctx.Request.DataAsString;
        var obj = JsonSerializer.Deserialize<T>(body, _jsonOptions);
        if (obj == null) throw new InvalidOperationException($"Invalid JSON for {typeof(T).Name}");
        return obj;
    }

    private static string? Q(HttpContextBase ctx, string key) => ctx.Request.RetrieveQueryValue(key);

    private static Guid? QGuid(HttpContextBase ctx, string key)
    {
        var s = Q(ctx, key);
        return Guid.TryParse(s, out var g) ? g : null;
    }

    private static T EnumQ<T>(HttpContextBase ctx, string key, T def = default!) where T : struct
    {
        var s = Q(ctx, key);
        return Enum.TryParse<T>(s, true, out var v) ? v : def;
    }

    private void AddStatic(HttpMethod method, string path, Func<HttpContextBase, Task> handler)
    {
        _server.Routes.PreAuthentication.Static.Add(method, path, async ctx => await handler(ctx));
        _server.Routes.PreAuthentication.Static.Add(HttpMethod.OPTIONS, path, Options);
    }

    private void AddParam(HttpMethod method, string path, Func<HttpContextBase, Task> handler)
    {
        _server.Routes.PreAuthentication.Parameter.Add(method, path, async ctx => await handler(ctx));
        _server.Routes.PreAuthentication.Parameter.Add(HttpMethod.OPTIONS, path, Options);
    }

    private void MapRoutes()
    {
        MapAppRoutes();
        MapClientProfileRoutes();
        MapAccountRoutes();
        MapBillingRoutes();
    }

    private void MapAppRoutes()
    {
        var app = new AppController();

        AddStatic(HttpMethod.PATCH, "/api/app/configure", async ctx =>
        {
            var body = ReadJson<ConfigParams>(ctx);
            var res = await app.Configure(body);
            await SendJson(ctx, res);
        });

        AddStatic(HttpMethod.GET, "/api/app/config", async ctx =>
        {
            var res = await app.GetConfig();
            await SendJson(ctx, res);
        });

        AddStatic(HttpMethod.GET, "/api/app/ip-filters", async ctx =>
        {
            var res = await app.GetIpFilters();
            await SendJson(ctx, res);
        });

        AddStatic(HttpMethod.PUT, "/api/app/ip-filters", async ctx =>
        {
            var body = ReadJson<IpFilters>(ctx);
            await app.SetIpFilters(body);
            await SendJson(ctx, new { ok = true });
        });

        AddStatic(HttpMethod.GET, "/api/app/state", async ctx =>
        {
            var res = await app.GetState();
            await SendJson(ctx, res);
        });

        AddStatic(HttpMethod.POST, "/api/app/connect", async ctx =>
        {
            await app.Connect(QGuid(ctx, "clientProfileId"), Q(ctx, "serverLocation"), EnumQ(ctx, "planId", VpnHood.Core.Common.Tokens.ConnectPlanId.Normal));
            await SendJson(ctx, new { ok = true });
        });

        AddStatic(HttpMethod.POST, "/api/app/diagnose", async ctx =>
        {
            await app.Diagnose(QGuid(ctx, "clientProfileId"), Q(ctx, "serverLocation"), EnumQ(ctx, "planId", VpnHood.Core.Common.Tokens.ConnectPlanId.Normal));
            await SendJson(ctx, new { ok = true });
        });

        AddStatic(HttpMethod.POST, "/api/app/disconnect", async ctx =>
        {
            await app.Disconnect();
            await SendJson(ctx, new { ok = true });
        });

        AddStatic(HttpMethod.POST, "/api/app/version-check", async ctx =>
        {
            await app.VersionCheck();
            await SendJson(ctx, new { ok = true });
        });

        AddStatic(HttpMethod.POST, "/api/app/version-check-postpone", async ctx =>
        {
            await app.VersionCheckPostpone();
            await SendJson(ctx, new { ok = true });
        });

        AddStatic(HttpMethod.POST, "/api/app/clear-last-error", async ctx =>
        {
            await app.ClearLastError();
            await SendJson(ctx, new { ok = true });
        });

        AddStatic(HttpMethod.POST, "/api/app/extend-by-rewarded-ad", async ctx =>
        {
            await app.ExtendByRewardedAd();
            await SendJson(ctx, new { ok = true });
        });

        AddStatic(HttpMethod.PUT, "/api/app/user-settings", async ctx =>
        {
            var body = ReadJson<VpnHood.AppLib.Settings.UserSettings>(ctx);
            await app.SetUserSettings(body);
            await SendJson(ctx, new { ok = true });
        });

        AddStatic(HttpMethod.GET, "/api/app/log.txt", async ctx =>
        {
            var text = await app.Log();
            Cors(ctx);
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.Send(text);
        });

        AddStatic(HttpMethod.GET, "/api/app/installed-apps", async ctx =>
        {
            var res = await app.GetInstalledApps();
            await SendJson(ctx, res);
        });

        AddStatic(HttpMethod.POST, "/api/app/user-review", async ctx =>
        {
            var body = ReadJson<AppUserReview>(ctx);
            await app.SetUserReview(body);
            await SendJson(ctx, new { ok = true });
        });

        AddStatic(HttpMethod.POST, "/api/app/internal-ad/dismiss", async ctx =>
        {
            var s = Q(ctx, "result");
            var ok = Enum.TryParse<VpnHood.AppLib.Abstractions.ShowAdResult>(s, true, out var result);
            await app.InternalAdDismiss(ok ? result : default);
            await SendJson(ctx, new { ok = true });
        });

        AddStatic(HttpMethod.POST, "/api/app/internal-ad/error", async ctx =>
        {
            await app.InternalAdError(Q(ctx, "errorMessage") ?? "Unknown");
            await SendJson(ctx, new { ok = true });
        });

        AddStatic(HttpMethod.POST, "/api/app/remove-premium", async ctx =>
        {
            var id = QGuid(ctx, "profileId") ?? Guid.Empty;
            await app.RemovePremium(id);
            await SendJson(ctx, new { ok = true });
        });

        AddStatic(HttpMethod.GET, "/api/app/countries", async ctx =>
        {
            var res = await app.GetCountries();
            await SendJson(ctx, res);
        });
    }

    private void MapClientProfileRoutes()
    {
        var ctrl = new ClientProfileController();

        AddStatic(HttpMethod.PUT, "/api/client-profiles/access-keys", async ctx =>
        {
            var accessKey = Q(ctx, "accessKey") ?? string.Empty;
            var res = await ctrl.AddByAccessKey(accessKey);
            await SendJson(ctx, res);
        });

        AddParam(HttpMethod.GET, "/api/client-profiles/{id}", async ctx =>
        {
            if (!Guid.TryParse(ctx.Request.Url.Parameters["id"], out var id)) { ctx.Response.StatusCode = 400; await ctx.Response.Send(); return; }
            var res = await ctrl.Get(id);
            await SendJson(ctx, res);
        });

        AddParam(HttpMethod.PATCH, "/api/client-profiles/{id}", async ctx =>
        {
            if (!Guid.TryParse(ctx.Request.Url.Parameters["id"], out var id)) { ctx.Response.StatusCode = 400; await ctx.Response.Send(); return; }
            var body = ReadJson<ClientProfileUpdateParams>(ctx);
            var res = await ctrl.Update(id, body);
            await SendJson(ctx, res);
        });

        AddParam(HttpMethod.DELETE, "/api/client-profiles/{id}", async ctx =>
        {
            if (!Guid.TryParse(ctx.Request.Url.Parameters["id"], out var id)) { ctx.Response.StatusCode = 400; await ctx.Response.Send(); return; }
            await ctrl.Delete(id);
            await SendJson(ctx, new { ok = true });
        });
    }

    private void MapAccountRoutes()
    {
        var ctrl = new AccountController();

        AddStatic(HttpMethod.GET, "/api/account", async ctx =>
        {
            var res = await ctrl.Get();
            await SendJson(ctx, res);
        });

        AddStatic(HttpMethod.POST, "/api/account/refresh", async ctx =>
        {
            await ctrl.Refresh();
            await SendJson(ctx, new { ok = true });
        });

        AddStatic(HttpMethod.GET, "/api/account/is-signin-with-google-supported", async ctx =>
        {
            var res = ctrl.IsSigninWithGoogleSupported();
            await SendJson(ctx, res);
        });

        AddStatic(HttpMethod.POST, "/api/account/signin-with-google", async ctx =>
        {
            await ctrl.SignInWithGoogle();
            await SendJson(ctx, new { ok = true });
        });

        AddStatic(HttpMethod.POST, "/api/account/sign-out", async ctx =>
        {
            await ctrl.SignOut();
            await SendJson(ctx, new { ok = true });
        });

        AddParam(HttpMethod.GET, "/api/account/subscriptions/{subId}/access-keys", async ctx =>
        {
            var subId = ctx.Request.Url.Parameters["subId"] ?? string.Empty;
            var res = await ctrl.ListAccessKeys(subId);
            await SendJson(ctx, res);
        });
    }

    private void MapBillingRoutes()
    {
        var ctrl = new BillingController();

        AddStatic(HttpMethod.GET, "/api/billing/subscription-plans", async ctx =>
        {
            var res = await ctrl.GetSubscriptionPlans();
            await SendJson(ctx, res);
        });

        AddStatic(HttpMethod.POST, "/api/billing/purchase", async ctx =>
        {
            var planId = Q(ctx, "planId") ?? string.Empty;
            var res = await ctrl.Purchase(planId);
            await SendJson(ctx, res);
        });

        AddStatic(HttpMethod.GET, "/api/billing/purchase-options", async ctx =>
        {
            var res = await ctrl.GetPurchaseOptions();
            await SendJson(ctx, res);
        });
    }
}
