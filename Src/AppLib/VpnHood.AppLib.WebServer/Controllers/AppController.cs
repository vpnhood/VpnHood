using System.Globalization;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Settings;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class AppController : IAppController
{
    private static VpnHoodApp App => VpnHoodApp.Instance;

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
            IntentFeatures = new AppIntentFeatures(App.Services.UiProvider, App.Services.UserReviewProvider),
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

    public Task Connect(Guid? clientProfileId, string? serverLocation, ConnectPlanId planId)
    {
        return App.Connect(
            new ConnectOptions {
                ClientProfileId = clientProfileId,
                ServerLocation = serverLocation,
                PlanId = planId,
            });
    }

    public Task Diagnose(Guid? clientProfileId, string? serverLocation, ConnectPlanId planId)
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
                EnglishName = region.EnglishName,
            })
            .ToArray();

        return Task.FromResult(countryInfos);
    }
}