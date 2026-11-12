using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.Settings;
using VpnHood.AppLib.App.VpnProfiles;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Storages;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Settings;

/// <summary>
/// Provides the app's settings, read from the storage at the app's start (<see cref="VpnHoodApp.SettingsService"/>):
/// a file that cannot be read is logged and replaced by the defaults.
/// </summary>
public class AppSettingsService
{
    private const string FileNameSettings = "settings.json";
    private readonly Storage _storage;
    private readonly Lock _saveLock = new();

    // fetched again when gone, so plain files on every platform, not the storage's
    private string RemoteSettingsFilePath => Path.Combine(_storage.FolderPath, "remote_settings.json");
    private string PromotionFolderPath => Path.Combine(_storage.FolderPath, "promotions");

    internal event EventHandler? BeforeSave;
    internal AppSettings Settings { get; }
    internal UserSettings UserSettings => Settings.UserSettings;
    internal UserSettings OldUserSettings { get; private set; }
    internal RemoteSettings? RemoteSettings { get; private set; }
    internal SplitIpViaAppSettings SplitIpViaAppSettings { get; }
    internal SplitIpViaDeviceSettings SplitIpViaDeviceSettings { get; }
    internal SplitDomainSettings SplitDomainSettings { get; }

    internal AppSettingsService(Storage storage, Uri? remoteSettingsUrl)
    {
        _storage = storage;
#pragma warning disable CS0618 // Type or member is obsolete
        ClientProfileMigration.MigrateSettings(Path.Combine(storage.FolderPath, FileNameSettings));
        KillSwitchSettingsMigration.MigrateSettings(Path.Combine(storage.FolderPath, FileNameSettings));
        TcpForwardingSettingsMigration.MigrateSettings(storage, FileNameSettings);
#pragma warning restore CS0618 // Type or member is obsolete

        // A file that cannot be read is replaced by defaults - a new client id among them - so the
        // failure must at least be in the log, or a device that keeps forgetting its settings has
        // nothing to show for it.
        Settings = storage.TryReadJson<AppSettings>(FileNameSettings, logger: VhLogger.Instance) ??
                   new AppSettings();
        Settings.AppSettingsService = this;
        var splitStorage = storage.OpenStorage("splits");
        SplitIpViaAppSettings = new SplitIpViaAppSettings(splitStorage.OpenStorage("ips_via_app"));
        SplitIpViaDeviceSettings = new SplitIpViaDeviceSettings(splitStorage.OpenStorage("ips_via_device"));
        SplitDomainSettings = new SplitDomainSettings(splitStorage.OpenStorage("domains"));
        OldUserSettings = Settings.UserSettings;

        // load remote settings
        if (remoteSettingsUrl != null) {
            RemoteSettings = JsonUtils.TryDeserializeFile<RemoteSettings>(RemoteSettingsFilePath);
            Task.Run(() => TryUpdateRemoteSettings(remoteSettingsUrl, CancellationToken.None));
        }
    }

    // get promotion image file path if exists
    internal string? PromotionImageFilePath {
        get {
            if (RemoteSettings?.PromotionImageUrl is null)
                return null;

            var filePath = BuildPromoteImageFilePath(RemoteSettings.PromotionImageUrl);
            return File.Exists(filePath) ? filePath : null;
        }
    }

    private string BuildPromoteImageFilePath(Uri imageUrl)
    {
        return Path.Combine(PromotionFolderPath, "image" + Path.GetExtension(imageUrl.AbsolutePath));
    }

    /// <summary>
    /// Saves the settings: it sets off applying them to the running app, some of them only at the next connect, then
    /// writes them with the time of the save (<see cref="AppSettings.ConfigTime"/>).
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The process may not write the file.</exception>
    /// <exception cref="IOException">
    /// The file could not be written, as where the device keeps too little room for it.
    /// </exception>
    public void Save()
    {
        BeforeSave?.Invoke(this, EventArgs.Empty);
        OldUserSettings = JsonUtils.JsonClone(UserSettings);
        lock (_saveLock) {
            Settings.ConfigTime = DateTime.UtcNow; // full precision: the change detection above (see AppSettings)
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            _storage.Write(FileNameSettings, json);
        }
    }

    private async Task TryUpdateRemoteSettings(Uri url, CancellationToken cancellationToken)
    {
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
            await UpdateRemoteSettings(url, linkedCts.Token).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Failed to update remote settings.");
        }
    }

    private async Task UpdateRemoteSettings(Uri url, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Fetching remote options...");
        var httpClient = new HttpClient();
        var json = await httpClient.GetStringAsync(url, cancellationToken).Vhc();
        RemoteSettings = JsonUtils.Deserialize<RemoteSettings>(json);
        await File.WriteAllTextAsync(RemoteSettingsFilePath, json, Encoding.UTF8, cancellationToken).Vhc();
        VhLogger.Instance.LogDebug("Remote update successfully.");

        // download promotion image
        await VhInvoker.TryInvokeAsync("update the promotion image", LogLevel.Debug, () =>
            UpdatePromoteImage(httpClient, RemoteSettings.PromotionImageUrl, cancellationToken)).Vhc();
    }

    private async Task UpdatePromoteImage(HttpClient httpClient, Uri? url, CancellationToken cancellationToken)
    {
        // delete all existing promotion images
        if (url == null) {
            if (Directory.Exists(PromotionFolderPath))
                Directory.Delete(PromotionFolderPath, true);
            return;
        }

        // download new promotion image
        Directory.CreateDirectory(PromotionFolderPath);
        VhLogger.Instance.LogDebug("Downloading promotion image from {0}", url);
        var imageBytes = await httpClient.GetByteArrayAsync(url, cancellationToken).Vhc();
        var fileName = BuildPromoteImageFilePath(url);
        await File.WriteAllBytesAsync(fileName, imageBytes, cancellationToken).Vhc();
        VhLogger.Instance.LogDebug("Promotion image saved to {0}", fileName);
    }
}