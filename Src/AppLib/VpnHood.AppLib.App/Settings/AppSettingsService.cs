using System.Text;
using System.Text.Json;
using VpnHood.Core.Common.Utils;

namespace VpnHood.AppLib.Settings;

public class AppSettingsService
{
    private readonly string _storagePath;
    private readonly object _saveLock = new();
    private string AppSettingsFilePath => Path.Combine(_storagePath, "settings.json");

    public event EventHandler? BeforeSave;
    public AppSettings AppSettings { get; }
    public IpFilterSettings IpFilterSettings { get; }

    public AppSettingsService(string storagePath)
    {
        _storagePath = storagePath;
        AppSettings = VhUtil.JsonDeserializeFile<AppSettings>(AppSettingsFilePath) ?? new AppSettings();
        AppSettings.AppSettingsService = this;
        IpFilterSettings = new IpFilterSettings(Path.Combine(storagePath, "ip_filters"));
    }

    public void Save()
    {
        BeforeSave?.Invoke(this, EventArgs.Empty);
        lock (_saveLock) {
            AppSettings.ConfigTime = DateTime.Now;
            var json = JsonSerializer.Serialize(AppSettings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(AppSettingsFilePath, json, Encoding.UTF8);
        }
    }
}