using System.Globalization;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Settings;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Extensions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Utils;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class AppController : ControllerBase, IAppController
{
    private static VpnHoodApp App => VpnHoodApp.Instance;

    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/app/";

        mapper.AddStatic(HttpMethod.PATCH, baseUrl + "configure", async ctx => {
            var body = ctx.ReadJson<ConfigParams>();
            var res = await Configure(body);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "config", async ctx => {
            var res = await GetConfig();
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "ip-filters", async ctx => {
            var res = await GetIpFilters();
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.PUT, baseUrl + "ip-filters", async ctx => {
            var body = ctx.ReadJson<IpFilters>();
            await SetIpFilters(body);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "state", async ctx => {
            var res = await GetState();
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "connect", async ctx => {
            await Connect(
                ctx.GetQueryParameter<Guid?>("clientProfileId", null),
                ctx.GetQueryParameter<string?>("serverLocation", null),
                ctx.GetQueryParameter("planId", ConnectPlanId.Normal));
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "diagnose", async ctx => {
            await Diagnose(
                ctx.GetQueryParameter<Guid?>("clientProfileId", null),
                ctx.GetQueryParameter<string?>("serverLocation", null),
                ctx.GetQueryParameter("planId", ConnectPlanId.Normal));
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "disconnect", async ctx => {
            await Disconnect();
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "version-check", async ctx => {
            await VersionCheck();
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "version-check-postpone", async ctx => {
            await VersionCheckPostpone();
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "clear-last-error", async ctx => {
            await ClearLastError();
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "extend-by-rewarded-ad", async ctx => {
            await ExtendByRewardedAd();
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.PUT, baseUrl + "user-settings", async ctx => {
            var body = ctx.ReadJson<UserSettings>();
            await SetUserSettings(body);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "log.txt", async ctx => {
            var text = await Log();
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.Send(text);
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "promotion.jpg", async ctx => {
            var imageBytes = await PromotionImage();
            ctx.Response.ContentType = "image/jpeg";
            await ctx.Response.Send(imageBytes);
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "installed-apps", async ctx => {
            var res = await GetInstalledApps();
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "user-review", async ctx => {
            var body = ctx.ReadJson<AppUserReview>();
            await SetUserReview(body);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "internal-ad/dismiss", async ctx => {
            var s = ctx.GetQueryParameter<string?>("result");
            var ok = Enum.TryParse<ShowAdResult>(s, true, out var result);
            await InternalAdDismiss(ok ? result : default);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "internal-ad/error", async ctx => {
            await InternalAdError(ctx.GetQueryParameter<string>("errorMessage"));
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "remove-premium", async ctx => {
            var id = ctx.GetQueryParameter<Guid>("profileId");
            await RemovePremium(id);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "countries", async ctx => {
            var res = await GetCountries();
            await ctx.SendJson(res);
        });
    }

    public async Task<AppData> Configure(ConfigParams configParams)
    {
        App.Services.CultureProvider.AvailableCultures = configParams.AvailableCultures;
        if (configParams.Strings != null)
            App.Resources.Strings = configParams.Strings;

        App.UpdateUi();
        return await GetConfig().Vhc();
    }

    public Task<AppData> GetConfig()
    {
        var ret = new AppData {
            Features = App.Features,
            IntentFeatures = new DeviceIntentFeatures(App.Services.DeviceUiProvider, App.Services.UserReviewProvider),
            UserSettings = App.UserSettings,
            ClientProfileInfos = App.ClientProfileService.List().Select(x => x.ToInfo()).ToArray(),
            State = App.State,
            AvailableCultureInfos = App.Services.CultureProvider.AvailableCultures
                .Select(x => new UiCultureInfo(x))
                .ToArray()
        };

        return Task.FromResult(ret);
    }

    public Task<IpFilters> GetIpFilters()
    {
        var appIpFilters = new IpFilters {
            AdapterIpFilterIncludes = App.SettingsService.IpFilterSettings.AdapterIpFilterIncludes,
            AdapterIpFilterExcludes = App.SettingsService.IpFilterSettings.AdapterIpFilterExcludes,
            AppIpFilterIncludes = App.SettingsService.IpFilterSettings.AppIpFilterIncludes,
            AppIpFilterExcludes = App.SettingsService.IpFilterSettings.AppIpFilterExcludes
        };

        return Task.FromResult(appIpFilters);
    }

    public Task SetIpFilters(IpFilters ipFilters)
    {
        App.SettingsService.IpFilterSettings.AdapterIpFilterExcludes = ipFilters.AdapterIpFilterExcludes;
        App.SettingsService.IpFilterSettings.AdapterIpFilterIncludes = ipFilters.AdapterIpFilterIncludes;
        App.SettingsService.IpFilterSettings.AppIpFilterExcludes = ipFilters.AppIpFilterExcludes;
        App.SettingsService.IpFilterSettings.AppIpFilterIncludes = ipFilters.AppIpFilterIncludes;
        return Task.CompletedTask;
    }

    public Task<AppState> GetState()
    {
        return Task.FromResult(App.State);
    }

    public Task Connect(Guid? clientProfileId,
        string? serverLocation, ConnectPlanId planId = ConnectPlanId.Normal)
    {
        return App.Connect(
            new ConnectOptions {
                ClientProfileId = clientProfileId,
                ServerLocation = serverLocation,
                PlanId = planId
            });
    }

    public Task Diagnose(Guid? clientProfileId, string? serverLocation,
        ConnectPlanId planId = ConnectPlanId.Normal)
    {
        return App.Connect(
            new ConnectOptions {
                ClientProfileId = clientProfileId,
                ServerLocation = serverLocation,
                PlanId = planId,
                Diagnose = true
            });
    }

    public Task Disconnect()
    {
        return App.Disconnect();
    }

    public Task VersionCheck()
    {
        if (App.Services.UpdaterService is null)
            throw new NotSupportedException("App Updater is not supported.");

        return App.Services.UpdaterService.CheckForUpdate(true, CancellationToken.None);
    }

    public Task VersionCheckPostpone()
    {
        if (App.Services.UpdaterService is null)
            throw new NotSupportedException("App Updater is not supported.");

        App.Services.UpdaterService.Postpone();
        return Task.CompletedTask;
    }

    public Task ClearLastError()
    {
        App.ClearLastError();
        return Task.CompletedTask;
    }

    public Task ExtendByRewardedAd()
    {
        return App.AdManager.ExtendByRewardedAd(CancellationToken.None);
    }

    public Task SetUserSettings(UserSettings userSettings)
    {
        App.SettingsService.Settings.UserSettings = userSettings;
        App.SettingsService.Save();
        return Task.CompletedTask;
    }

    public async Task<string> Log()
    {
        await using var ms = new MemoryStream();
        await App.CopyLogToStream(ms).Vhc();
        ms.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(ms);
        return await reader.ReadToEndAsync();
    }

    public async Task<byte[]> PromotionImage()
    {
        if (App.SettingsService.PromotionImageFilePath is null || !File.Exists(App.SettingsService.PromotionImageFilePath))
            throw new NotExistsException();

        return await File.ReadAllBytesAsync(App.SettingsService.PromotionImageFilePath);
    }

    public Task<DeviceAppInfo[]> GetInstalledApps()
    {
        return Task.FromResult(App.InstalledApps);
    }

    public Task ProcessTypes(ExceptionType exceptionType, SessionErrorCode errorCode)
    {
        throw new NotSupportedException("This method exists just to let swagger generate types.");
    }

    public Task SetUserReview(AppUserReview userReview)
    {
        App.SetUserReview(userReview.Rating, userReview.ReviewText);
        return Task.CompletedTask;
    }

    public Task InternalAdDismiss(ShowAdResult result)
    {
        App.AdManager.AdService.InternalAdDismiss(result);
        return Task.CompletedTask;
    }

    public Task InternalAdError(string errorMessage)
    {
        App.AdManager.AdService.InternalAdError(new Exception(errorMessage));
        return Task.CompletedTask;
    }

    public Task RemovePremium(Guid profileId)
    {
        App.RemovePremium(profileId);
        return Task.CompletedTask;
    }

    public Task<CountryInfo[]> GetCountries()
    {
        var countryInfos = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(culture => new RegionInfo(culture.Name))
            .Where(region => !string.IsNullOrEmpty(region.Name))
            .DistinctBy(region => region.Name)
            .OrderBy(region => region.EnglishName)
            .Select(region => new CountryInfo {
                CountryCode = region.Name,
                EnglishName = region.EnglishName
            })
            .ToArray();

        return Task.FromResult(countryInfos);
    }
}