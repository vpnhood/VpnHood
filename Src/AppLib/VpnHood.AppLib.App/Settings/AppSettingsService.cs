using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Settings;

public class AppSettingsService
{
    private readonly string _storagePath;
    private readonly object _saveLock = new();
    private string AppSettingsFilePath => Path.Combine(_storagePath, "settings.json");
    private string RemoteSettingsFilePath => Path.Combine(_storagePath, "remote_settings.json");

    public event EventHandler? BeforeSave;
    public AppSettings Settings { get; }
    public RemoteSettings? RemoteSettings { get; private set; }
    public IpFilterSettings IpFilterSettings { get; }

    public AppSettingsService(string storagePath, Uri? remoteSettingsUrl)
    {
        _storagePath = storagePath;
        Settings = JsonUtils.TryDeserializeFile<AppSettings>(AppSettingsFilePath) ?? new AppSettings();
        Settings.AppSettingsService = this;
        IpFilterSettings = new IpFilterSettings(Path.Combine(storagePath, "ip_filters"));

        // load remote settings
        if (remoteSettingsUrl != null) {
            RemoteSettings = JsonUtils.TryDeserializeFile<RemoteSettings>(RemoteSettingsFilePath);
            Task.Run(() => TryUpdateRemoteSettings(remoteSettingsUrl));
        }
    }

    public void Save()
    {
        BeforeSave?.Invoke(this, EventArgs.Empty);
        lock (_saveLock) {
            Settings.ConfigTime = DateTime.Now;
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(AppSettingsFilePath, json, Encoding.UTF8);
        }
    }

    public async Task TryUpdateRemoteSettings(Uri url)
    {
        try {
            VhLogger.Instance.LogDebug("Fetching remote options...");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var httpClient = new HttpClient();
            var json = await httpClient.GetStringAsync(url, cts.Token);
            RemoteSettings = JsonUtils.Deserialize<RemoteSettings>(json);
            await File.WriteAllTextAsync(RemoteSettingsFilePath, json, Encoding.UTF8, cts.Token);
            VhLogger.Instance.LogDebug("Remote update successfully.");
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Failed to fetch remote options.");
        }
    }
}