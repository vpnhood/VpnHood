using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.Settings;

public class AppSettings
{
    private readonly object _saveLock = new();
    public event EventHandler? BeforeSave;

    [JsonIgnore] public string SettingsFilePath { get; private set; } = null!;
    public int Version { get; set; } = 1;
    public bool? IsQuickLaunchEnabled { get; set; }
    public bool? IsNotificationEnabled { get; set; }
    public DateTime ConfigTime { get; set; } = DateTime.Now;
    public UserSettings UserSettings { get; set; } = new();
    public Guid ClientId { get; set; } = Guid.NewGuid();

    public void Save()
    {
        BeforeSave?.Invoke(this, EventArgs.Empty);

        lock (_saveLock) {
            ConfigTime = DateTime.Now;
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json, Encoding.UTF8);
        }
    }

    internal static AppSettings Load(string settingsFilePath)
    {
        var res = VhUtil.JsonDeserializeFile<AppSettings>(settingsFilePath) ?? new AppSettings();
        if (res.Version < 2) res.UserSettings.CultureCode = null;
        res.Version = 2;
        res.SettingsFilePath = settingsFilePath;
        return res;
    }
}