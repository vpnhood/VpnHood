using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Dtos;
using VpnHood.AppLib.Settings;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Helpers;
using VpnHood.Core.Client.Devices;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Utils;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class AppController(VpnHoodApp app) : ControllerBase, IAppController
{
    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/app/";

        mapper.AddStatic(HttpMethod.PATCH, baseUrl + "configure", async ctx => {
            var body = ctx.ReadJson<ConfigParams>();
            var res = await Configure(body, ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "config", async ctx => {
            var res = await GetConfig(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "split-by-ips", async ctx => {
            var res = await GetSplitIps(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.PUT, baseUrl + "split-by-ips", async ctx => {
            var body = ctx.ReadJson<SplitIps>();
            await SetSplitIps(body, ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "split-by-domains", async ctx => {
            var res = await GetSplitDomains(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.PUT, baseUrl + "split-by-domains", async ctx => {
            var body = ctx.ReadJson<SplitDomains>();
            await SetSplitDomains(body, ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "state", async ctx => {
            var res = await GetState(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "connect", async ctx => {
            await Connect(
                ctx.GetQueryParameter<Guid?>("clientProfileId", null),
                ctx.GetQueryParameter<string?>("serverLocation", null),
                ctx.GetQueryParameter("planId", ConnectPlanId.Normal),
                ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "diagnose", async ctx => {
            await Diagnose(
                ctx.GetQueryParameter<Guid?>("clientProfileId", null),
                ctx.GetQueryParameter<string?>("serverLocation", null),
                ctx.GetQueryParameter("planId", ConnectPlanId.Normal),
                ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "disconnect", async ctx => {
            await Disconnect(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "version-check", async ctx => {
            await VersionCheck(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "version-check-postpone", async ctx => {
            await VersionCheckPostpone(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "clear-last-error", async ctx => {
            await ClearLastError(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "extend-by-rewarded-ad", async ctx => {
            await ExtendByRewardedAd(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.PUT, baseUrl + "user-settings", async ctx => {
            var body = ctx.ReadJson<UserSettings>();
            await SetUserSettings(body, ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "log.txt", async ctx => {
            var text = await Log(ctx.Token);
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.Send(text);
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "promotion.jpg", async ctx => {
            var imageBytes = await PromotionImage(ctx.Token);
            ctx.Response.ContentType = "image/jpeg";
            await ctx.Response.Send(imageBytes);
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "installed-apps", async ctx => {
            var res = await GetInstalledApps(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "user-review", async ctx => {
            var body = ctx.ReadJson<AppUserReview>();
            await SetUserReview(body, ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "internal-ad/dismiss", async ctx => {
            var s = ctx.GetQueryParameter<string?>("result");
            var ok = Enum.TryParse<ShowAdResult>(s, true, out var result);
            await InternalAdDismiss(ok ? result : default, ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "internal-ad/error", async ctx => {
            await InternalAdError(ctx.GetQueryParameter<string>("errorMessage"), ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "remove-premium", async ctx => {
            var id = ctx.GetQueryParameter<Guid>("profileId");
            await RemovePremium(id, ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "countries", async ctx => {
            var res = await GetCountries(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "supported-split-by-countries", async ctx => {
            var res = await GetSupportedSplitCountries(ctx.Token);
            await ctx.SendJson(res);
        });
    }

    public async Task<AppData> Configure(ConfigParams configParams, CancellationToken cancellationToken)
    {
        app.Services.CultureProvider.AvailableCultures = configParams.AvailableCultures;
        if (configParams.Strings != null)
            app.Resources.Strings = configParams.Strings;

        app.UpdateUi();
        return await GetConfig(cancellationToken).Vhc();
    }

    public Task<AppData> GetConfig(CancellationToken cancellationToken)
    {
        var ret = new AppData {
            Features = app.Features,
            IntentFeatures = new DeviceIntentFeatures(app.Services.DeviceUiProvider, app.Services.UserReviewProvider),
            UserSettings = app.UserSettings,
            ClientProfileInfos = app.ClientProfileService.List().Select(x => x.ToInfo(app.Features)).ToArray(),
            State = app.State,
            AvailableCultureInfos = app.Services.CultureProvider.AvailableCultures
                .Select(x => new UiCultureInfo(x))
                .ToArray()
        };

        return Task.FromResult(ret);
    }

    public Task<SplitIps> GetSplitIps(CancellationToken cancellationToken)
    {
        var appIpFilters = new SplitIps {
            DeviceIncludes = app.SettingsService.SplitIpSettings.DeviceIncludes,
            DeviceExcludes = app.SettingsService.SplitIpSettings.DeviceExcludes,
            AppIncludes = app.SettingsService.SplitIpSettings.AppIncludes,
            AppExcludes = app.SettingsService.SplitIpSettings.AppExcludes,
            AppBlocks = app.SettingsService.SplitIpSettings.AppBlocks
        };

        return Task.FromResult(appIpFilters);
    }

    public Task SetSplitIps(SplitIps value, CancellationToken cancellationToken)
    {
        app.SettingsService.SplitIpSettings.DeviceExcludes = value.DeviceExcludes;
        app.SettingsService.SplitIpSettings.DeviceIncludes = value.DeviceIncludes;
        app.SettingsService.SplitIpSettings.AppExcludes = value.AppExcludes;
        app.SettingsService.SplitIpSettings.AppIncludes = value.AppIncludes;
        app.SettingsService.SplitIpSettings.AppBlocks = value.AppBlocks;

        // the text files are not part of UserSettings, so live-apply to a running session explicitly
        _ = VhUtils.TryInvokeAsync("Reconfigure after split-ip change", app.ReconfigureVpnService);
        return Task.CompletedTask;
    }

    public Task<SplitDomains> GetSplitDomains(CancellationToken cancellationToken)
    {
        var splitByDomains = new SplitDomains {
            Includes = app.SettingsService.SplitDomainSettings.Includes,
            Excludes = app.SettingsService.SplitDomainSettings.Excludes,
            Blocks = app.SettingsService.SplitDomainSettings.Blocks
        };

        return Task.FromResult(splitByDomains);
    }

    public Task SetSplitDomains(SplitDomains value, CancellationToken cancellationToken)
    {
        app.SettingsService.SplitDomainSettings.Includes = value.Includes;
        app.SettingsService.SplitDomainSettings.Excludes = value.Excludes;
        app.SettingsService.SplitDomainSettings.Blocks = value.Blocks;

        // the text files are not part of UserSettings, so live-apply to a running session explicitly
        _ = VhUtils.TryInvokeAsync("Reconfigure after split-domain change", app.ReconfigureVpnService);
        return Task.CompletedTask;
    }

    public Task<AppState> GetState(CancellationToken cancellationToken)
    {
        return Task.FromResult(app.State);
    }

    public Task Connect(Guid? clientProfileId,
        string? serverLocation, ConnectPlanId planId, CancellationToken cancellationToken)
    {
        return app.Connect(
            new ConnectOptions {
                ClientProfileId = clientProfileId,
                ServerLocation = serverLocation,
                PlanId = planId
            }, cancellationToken);
    }

    public Task Diagnose(Guid? clientProfileId, string? serverLocation,
        ConnectPlanId planId, CancellationToken cancellationToken)
    {
        return app.Connect(
            new ConnectOptions {
                ClientProfileId = clientProfileId,
                ServerLocation = serverLocation,
                PlanId = planId,
                Diagnose = true
            }, cancellationToken);
    }

    public Task Disconnect(CancellationToken cancellationToken)
    {
        return app.Disconnect();
    }

    public Task VersionCheck(CancellationToken cancellationToken)
    {
        if (app.Services.UpdaterService is null)
            throw new NotSupportedException("App Updater is not supported.");

        return app.Services.UpdaterService.CheckForUpdate(true, cancellationToken);
    }

    public Task VersionCheckPostpone(CancellationToken cancellationToken)
    {
        if (app.Services.UpdaterService is null)
            throw new NotSupportedException("App Updater is not supported.");

        app.Services.UpdaterService.Postpone();
        return Task.CompletedTask;
    }

    public Task ClearLastError(CancellationToken cancellationToken)
    {
        app.ClearLastError();
        return Task.CompletedTask;
    }

    public Task ExtendByRewardedAd(CancellationToken cancellationToken)
    {
        return app.AdManager.ExtendByRewardedAd(cancellationToken);
    }

    public Task SetUserSettings(UserSettings userSettings, CancellationToken cancellationToken)
    {
        app.SettingsService.Settings.UserSettings = userSettings;
        app.SettingsService.Save();
        return Task.CompletedTask;
    }

    public async Task<string> Log(CancellationToken cancellationToken)
    {
        await using var ms = new MemoryStream();
        await app.CopyLogToStream(ms).Vhc();
        ms.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(ms);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public async Task<byte[]> PromotionImage(CancellationToken cancellationToken)
    {
        if (app.SettingsService.PromotionImageFilePath is null ||
            !File.Exists(app.SettingsService.PromotionImageFilePath))
            throw new NotExistsException();

        return await File.ReadAllBytesAsync(app.SettingsService.PromotionImageFilePath, cancellationToken);
    }

    public Task<DeviceAppInfo[]> GetInstalledApps(CancellationToken cancellationToken)
    {
        return Task.FromResult(app.InstalledApps);
    }

    public Task ProcessTypes(ExceptionType exceptionType, SessionErrorCode errorCode, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This method exists just to let swagger generate types.");
    }

    public Task SetUserReview(AppUserReview userReview, CancellationToken cancellationToken)
    {
        app.SetUserReview(userReview.Rating, userReview.ReviewText);
        return Task.CompletedTask;
    }

    public Task InternalAdDismiss(ShowAdResult result, CancellationToken cancellationToken)
    {
        app.AdManager.AdService.InternalAdDismiss(result);
        return Task.CompletedTask;
    }

    public Task InternalAdError(string errorMessage, CancellationToken cancellationToken)
    {
        app.AdManager.AdService.InternalAdError(new Exception(errorMessage));
        return Task.CompletedTask;
    }

    public Task RemovePremium(Guid profileId, CancellationToken cancellationToken)
    {
        app.RemovePremium(profileId);
        return Task.CompletedTask;
    }

    public Task<CountryInfo[]> GetCountries(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult(AppCountryInfo.GetAll());
    }

    public Task<CountryInfo[]> GetSupportedSplitCountries(CancellationToken cancellationToken)
    {
        return app.Services.SplitCountryService.GetSupportedSplitCountries(cancellationToken);
    }
}