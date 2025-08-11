using System.Globalization;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Settings;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class AppController : WebApiController, IAppController
{
    private static VpnHoodApp App => VpnHoodApp.Instance;

    [Route(HttpVerbs.Patch, "/configure")]
    public async Task<AppData> Configure(ConfigParams configParams)
    {
        configParams = await HttpContext.GetRequestDataAsync<ConfigParams>().Vhc();
        App.Services.CultureProvider.AvailableCultures = configParams.AvailableCultures;
        if (configParams.Strings != null) 
            App.Resources.Strings = configParams.Strings;

        App.UpdateUi();
        return await GetConfig().Vhc();
    }

    [Route(HttpVerbs.Get, "/config")]
    public Task<AppData> GetConfig()
    {
        var ret = new AppData {
            Features = App.Features,
            Settings = App.Settings,
            ClientProfileInfos = App.ClientProfileService.List().Select(x => x.ToInfo()).ToArray(),
            State = App.State,
            AvailableCultureInfos = App.Services.CultureProvider.AvailableCultures
                .Select(x => new UiCultureInfo(x))
                .ToArray()
        };

        return Task.FromResult(ret);
    }

    [Route(HttpVerbs.Get, "/ip-filters")]
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

    [Route(HttpVerbs.Put, "/ip-filters")]
    public async Task SetIpFilters(IpFilters ipFilters)
    {
        ipFilters = await HttpContext.GetRequestDataAsync<IpFilters>().Vhc();
        App.SettingsService.IpFilterSettings.AdapterIpFilterExcludes = ipFilters.AdapterIpFilterExcludes;
        App.SettingsService.IpFilterSettings.AdapterIpFilterIncludes = ipFilters.AdapterIpFilterIncludes;
        App.SettingsService.IpFilterSettings.AppIpFilterExcludes = ipFilters.AppIpFilterExcludes;
        App.SettingsService.IpFilterSettings.AppIpFilterIncludes = ipFilters.AppIpFilterIncludes;
    }

    [Route(HttpVerbs.Get, "/state")]
    public Task<AppState> GetState()
    {
        return Task.FromResult(App.State);
    }

    [Route(HttpVerbs.Post, "/connect")]
    public Task Connect([QueryField] Guid? clientProfileId = null, [QueryField] string? serverLocation = null,
        [QueryField] ConnectPlanId planId = ConnectPlanId.Normal)
    {
        return App.Connect(
            new ConnectOptions {
                ClientProfileId = clientProfileId,
                ServerLocation = serverLocation,
                PlanId = planId,
                UserAgent = HttpContext.Request.UserAgent,
            });
    }

    [Route(HttpVerbs.Post, "/diagnose")]
    public Task Diagnose([QueryField] Guid? clientProfileId = null, [QueryField] string? serverLocation = null,
        [QueryField] ConnectPlanId planId = ConnectPlanId.Normal)
    {
        return App.Connect(
            new ConnectOptions {
                ClientProfileId = clientProfileId,
                ServerLocation = serverLocation,
                PlanId = planId,
                UserAgent = HttpContext.Request.UserAgent,
                Diagnose = true
            });
    }

    [Route(HttpVerbs.Post, "/disconnect")]
    public Task Disconnect()
    {
        return App.Disconnect();
    }

    [Route(HttpVerbs.Post, "/version-check")]
    public Task VersionCheck()
    {
        return App.VersionCheck(true, TimeSpan.Zero, HttpContext.CancellationToken);
    }

    [Route(HttpVerbs.Post, "/version-check-postpone")]
    public Task VersionCheckPostpone()
    {
        App.VersionCheckPostpone();
        return Task.CompletedTask;
    }

    [Route(HttpVerbs.Post, "/clear-last-error")]
    public Task ClearLastError()
    {
        App.ClearLastError();
        return Task.CompletedTask;
    }

    [Route(HttpVerbs.Post, "/extend-by-rewarded-ad")]
    public Task ExtendByRewardedAd()
    {
        return App.AdManager.ExtendByRewardedAd(HttpContext.CancellationToken);
    }

    [Route(HttpVerbs.Put, "/user-settings")]
    public async Task SetUserSettings(UserSettings userSettings)
    {
        userSettings = await HttpContext.GetRequestDataAsync<UserSettings>().Vhc();
        App.SettingsService.Settings.UserSettings = userSettings;
        App.SettingsService.Save();
    }

    [Route(HttpVerbs.Get, "/log.txt")]
    public async Task<string> Log()
    {
        Response.ContentType = MimeType.PlainText;
        var stream = HttpContext.OpenResponseStream(); // do not dispose, EmbedIO will do it
        await App.CopyLogToStream(stream).Vhc();
        HttpContext.SetHandled();
        return ""; // already wrote to stream
    }

    [Route(HttpVerbs.Get, "/installed-apps")]
    public Task<DeviceAppInfo[]> GetInstalledApps()
    {
        return Task.FromResult(App.InstalledApps);
    }

    [Route(HttpVerbs.Post, "/intents/request-always-on")]
    public Task RequestAlwaysOn()
    {
        App.Services.UiProvider.RequestAlwaysOn(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    [Route(HttpVerbs.Post, "/intents/request-quick-launch")]
    public Task RequestQuickLaunch()
    {
        return App.Services.UiProvider.RequestQuickLaunch(AppUiContext.RequiredContext, CancellationToken.None);
    }

    [Route(HttpVerbs.Post, "/intents/request-notification")]
    public Task RequestNotification()
    {
        return App.Services.UiProvider.RequestNotification(AppUiContext.RequiredContext, CancellationToken.None);
    }

    [Route(HttpVerbs.Post, "/intents/request-user-review")]
    public Task RequestUserReview()
    {
        if (App.Services.UserReviewProvider is null)
            throw new NotSupportedException("User review is not supported.");

        return App.Services.UserReviewProvider.RequestReview(AppUiContext.RequiredContext, CancellationToken.None);
    }


    [Route(HttpVerbs.Post, "/process-types")]
    public Task ProcessTypes(ExceptionType exceptionType, SessionErrorCode errorCode)
    {
        throw new NotSupportedException("This method exists just to let swagger generate types.");
    }

    [Route(HttpVerbs.Post, "/user-review")]
    public async Task SetUserReview(Api.UserReview userReview)
    {
        userReview = await HttpContext.GetRequestDataAsync<Api.UserReview>().Vhc();
        App.SetUserReview(userReview.Rating, userReview.ReviewText);
    }

    [Route(HttpVerbs.Get, "/countries")]
    public Task<CountryInfo[]> GetCountries()
    {
        var countryInfos = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(culture => new RegionInfo(culture.Name))
            .Where(region => !string.IsNullOrEmpty(region.Name))
            .DistinctBy(region => region.Name)
            .OrderBy(region => region.EnglishName)
            .Select(region => new CountryInfo {
                CountryCode = region.Name,
                EnglishName = region.EnglishName,
            })
            .ToArray();

        return Task.FromResult(countryInfos);
    }
}