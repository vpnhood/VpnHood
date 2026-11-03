using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Api.Settings;
using VpnHood.AppLib.App.VpnProfiles;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Settings;

public class AppSettingsService
{
    private readonly IAppFileStore _fileStore;
    private readonly string _storagePath;
    private readonly Lock _saveLock = new();
    private string AppSettingsFilePath => Path.Combine(_storagePath, "settings.json");

    // fetched again when gone, so plain files on every platform (IAppFileStore)
    private string RemoteSettingsFilePath => Path.Combine(_storagePath, "remote_settings.json");
    private string PromotionFolderPath => Path.Combine(_storagePath, "promotions");

    public event EventHandler? BeforeSave;
    public AppSettings Settings { get; }
    public UserSettings UserSettings => Settings.UserSettings;
    public UserSettings OldUserSettings { get; private set; }
    public RemoteSettings? RemoteSettings { get; private set; }
    public SplitIpViaAppSettings SplitIpViaAppSettings { get; }
    public SplitIpViaDeviceSettings SplitIpViaDeviceSettings { get; }
    public SplitDomainSettings SplitDomainSettings { get; }

    public AppSettingsService(IAppFileStore fileStore, string storagePath, Uri? remoteSettingsUrl)
    {
        _fileStore = fileStore;
        _storagePath = storagePath;
#pragma warning disable CS0618 // Type or member is obsolete
        ClientProfileMigration.MigrateSettings(AppSettingsFilePath);
#pragma warning restore CS0618 // Type or member is obsolete

        // A file that cannot be read is replaced by defaults - a new client id among them - so the
        // failure must at least be in the log, or a device that keeps forgetting its settings has
        // nothing to show for it.
        Settings = fileStore.TryReadJson<AppSettings>(AppSettingsFilePath, logger: VhLogger.Instance) ??
                   new AppSettings();
        Settings.AppSettingsService = this;
        SplitIpViaAppSettings = new SplitIpViaAppSettings(fileStore, Path.Combine(storagePath, "splits", "ips_via_app"));
        SplitIpViaDeviceSettings = new SplitIpViaDeviceSettings(fileStore, Path.Combine(storagePath, "splits", "ips_via_device"));
        SplitDomainSettings = new SplitDomainSettings(fileStore, Path.Combine(storagePath, "splits", "domains"));
        OldUserSettings = Settings.UserSettings;

        // load remote settings
        if (remoteSettingsUrl != null) {
            RemoteSettings = JsonUtils.TryDeserializeFile<RemoteSettings>(RemoteSettingsFilePath);
            Task.Run(() => TryUpdateRemoteSettings(remoteSettingsUrl, CancellationToken.None));
        }
    }

    // get promotion image file path if exists
    public string? PromotionImageFilePath {
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


    public void Save()
    {
        BeforeSave?.Invoke(this, EventArgs.Empty);
        OldUserSettings = JsonUtils.JsonClone(UserSettings);
        lock (_saveLock) {
            Settings.ConfigTime = DateTime.UtcNow; // full precision: the change detection above (see AppSettings)
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            _fileStore.Write(AppSettingsFilePath, json);
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