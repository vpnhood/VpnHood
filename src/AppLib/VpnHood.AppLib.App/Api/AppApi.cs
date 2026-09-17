using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Exceptions;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Api.Ads;
using VpnHood.AppLib.Api.Countries;
using VpnHood.AppLib.Api.Device;
using VpnHood.AppLib.Api.Sessions;
using VpnHood.AppLib.Api.Settings;
using VpnHood.AppLib.Api.SplitTunneling;
using VpnHood.AppLib.DtoConverters;
using VpnHood.AppLib.Services.Countries;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.AppLib.Api;

// The host is asked for, not held: this exists before any listener does - in process there may
// never be one - and only the three remote-access calls, the pairing page's, need it.
internal sealed class AppApi(VpnHoodApp app) : IAppApi
{
    private IRemoteAccessHost RemoteAccessHost => app.RemoteAccessHost ??
        throw new NotSupportedException("Remote access needs a web host, and this app was given none.");

    public async Task<AppInfo> Configure(ConfigParams configParams, CancellationToken cancellationToken)
    {
        app.Services.CultureProvider.AvailableCultures = configParams.AvailableCultures;
        if (configParams.Strings != null)
            app.Resources.Strings = configParams.Strings;

        app.UpdateUi();
        return await GetInfo(cancellationToken).Vhc();
    }

    public Task<AppInfo> GetInfo(CancellationToken cancellationToken)
    {
        var ret = new AppInfo {
            Features = app.Features,
            IntentFeatures = app.Services.DeviceUiProvider.ToIntentFeatures(app.Services.UserReviewProvider),
            UserSettings = app.UserSettings,
            ClientProfileInfos = [.. app.ClientProfileService.List().Select(x => x.ToInfo(app.Features))],
            State = app.State,
            AvailableCultureInfos = [
                .. app.Services.CultureProvider.AvailableCultures
                    .Select(x => new UiCultureInfo(x))
            ]
        };

        return Task.FromResult(ret);
    }

    public Task<SplitIpsViaApp> GetSplitIpsViaApp(CancellationToken cancellationToken)
    {
        return Task.FromResult(app.SettingsService.SplitIpViaAppSettings.Get());
    }

    public Task SetSplitIpsViaApp(SplitIpsViaApp value, CancellationToken cancellationToken)
    {
        app.SettingsService.SplitIpViaAppSettings.Set(value);
        return Task.CompletedTask;
    }

    public Task<SplitIpsViaDevice> GetSplitIpsViaDevice(CancellationToken cancellationToken)
    {
        return Task.FromResult(app.SettingsService.SplitIpViaDeviceSettings.Get());
    }

    public Task SetSplitIpsViaDevice(SplitIpsViaDevice value, CancellationToken cancellationToken)
    {
        app.SettingsService.SplitIpViaDeviceSettings.Set(value);
        return Task.CompletedTask;
    }

    public Task<SplitDomains> GetSplitDomains(CancellationToken cancellationToken)
    {
        return Task.FromResult(app.SettingsService.SplitDomainSettings.Get());
    }

    public Task SetSplitDomains(SplitDomains value, CancellationToken cancellationToken)
    {
        app.SettingsService.SplitDomainSettings.Set(value);
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

    public Task ClearReconnectRequired(CancellationToken cancellationToken)
    {
        app.ClearReconnectRequired();
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

    public Task<IReadOnlyList<DeviceAppInfo>> GetInstalledApps(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<DeviceAppInfo>>(
            [.. app.InstalledApps.Select(x => x.ToAppDto())]);
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

    public Task<CountryInfo[]> GetCountries(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult(AppCountryInfo.GetAll());
    }

    public Task<CountryInfo[]> GetSupportedSplitCountries(CancellationToken cancellationToken)
    {
        return app.Services.SplitCountryService.GetSupportedSplitCountries(cancellationToken);
    }

    // The pairing screen's poll: the network can change under an open screen, so this re-reads the
    // addresses and rebinds when they moved, besides answering with the presence list.
    public Task<RemoteAccessState> GetRemoteAccess(CancellationToken cancellationToken)
    {
        return RemoteAccessHost.RefreshRemoteAccess(cancellationToken);
    }

    public Task<RemoteAccessState> StartRemoteAccess(CancellationToken cancellationToken)
    {
        return RemoteAccessHost.StartRemoteAccess(cancellationToken);
    }

    public Task StopRemoteAccess(CancellationToken cancellationToken)
    {
        return RemoteAccessHost.StopRemoteAccess(cancellationToken);
    }
}