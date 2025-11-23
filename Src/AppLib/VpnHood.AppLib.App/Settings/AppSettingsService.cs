using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Settings;

public class AppSettingsService
{
    private readonly string _storagePath;
    private readonly Lock _saveLock = new();
    private string AppSettingsFilePath => Path.Combine(_storagePath, "settings.json");
    private string RemoteSettingsFilePath => Path.Combine(_storagePath, "remote_settings.json");
    private string PromotionFolderPath => Path.Combine(_storagePath, "promotions");

    public event EventHandler? BeforeSave;
    public AppSettings Settings { get; }
    public UserSettings UserSettings => Settings.UserSettings;
    public UserSettings OldUserSettings { get; private set; }
    public RemoteSettings? RemoteSettings { get; private set; }
    public IpFilterSettings IpFilterSettings { get; }

    public AppSettingsService(string storagePath, Uri? remoteSettingsUrl, bool debugMode)
    {
        _storagePath = storagePath;
        Settings = JsonUtils.TryDeserializeFile<AppSettings>(AppSettingsFilePath) ?? GetDefaultSettings(debugMode);
        Settings.AppSettingsService = this;
        IpFilterSettings = new IpFilterSettings(Path.Combine(storagePath, "ip_filters"));
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

    private static AppSettings GetDefaultSettings(bool debugMode)
    {
        return new AppSettings {
            UserSettings = {
                AllowRemoteAccess = debugMode // the default value of AllowRemoteAccess is true in debug mode 
            }
        };
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
            Settings.ConfigTime = DateTime.Now;
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(AppSettingsFilePath, json, Encoding.UTF8);
        }
    }

    private async Task TryUpdateRemoteSettings(Uri url, CancellationToken cancellationToken)
    {
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
            await UpdateRemoteSettings(url, linkedCts.Token);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Failed to update remote settings.");
        }
    }

    private async Task UpdateRemoteSettings(Uri url, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Fetching remote options...");
        var httpClient = new HttpClient();
        var json = await httpClient.GetStringAsync(url, cancellationToken);
        RemoteSettings = JsonUtils.Deserialize<RemoteSettings>(json);
        await File.WriteAllTextAsync(RemoteSettingsFilePath, json, Encoding.UTF8, cancellationToken);
        VhLogger.Instance.LogDebug("Remote update successfully.");

        // download promotion image
        await VhUtils.TryInvokeAsync("Update Promote Image", () => 
            UpdatePromoteImage(httpClient, RemoteSettings.PromotionImageUrl, cancellationToken));
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
        var imageBytes = await httpClient.GetByteArrayAsync(url, cancellationToken);
        var fileName = BuildPromoteImageFilePath(url);
        await File.WriteAllBytesAsync(fileName, imageBytes, cancellationToken);
        VhLogger.Instance.LogDebug("Promotion image saved to {0}", fileName);
    }
}