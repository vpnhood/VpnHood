using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Client.App.Settings;

public class AppSettings
{
    private readonly object _saveLock = new();
    public event EventHandler? Saved;

    [JsonIgnore] 
    public string SettingsFilePath { get; private set; } = null!;
    public int Version { get; set; } = 1;
    public bool IsQuickLaunchAdded { get; set; } 
    public bool IsQuickLaunchRequested { get; set; }
    public DateTime ConfigTime { get; set; } = DateTime.Now;
    public UserSettings UserSettings { get; set; } = new();
    public Guid ClientId { get; set; } = Guid.NewGuid();
    public string? LastCountryIpGroupId { get; set; }
    public DateTime? LastUpdateCheckTime { get; set; }


    public void Save()
    {
        lock (_saveLock)
        {
            ConfigTime = DateTime.Now;
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json, Encoding.UTF8);
        }

        Saved?.Invoke(this, EventArgs.Empty);
    }

    internal static AppSettings Load(string settingsFilePath)
    {
        try
        {
            var json = File.ReadAllText(settingsFilePath, Encoding.UTF8);
            var ret = JsonSerializer.Deserialize<AppSettings>(json) ??
                      throw new FormatException($"Could not deserialize {nameof(AppSettings)} from {settingsFilePath}");

            if (ret.Version < 2) ret.UserSettings.CultureCode = null;
            ret.Version = 2;
            ret.SettingsFilePath = settingsFilePath;
            return ret;
        }
        catch
        {
            var ret = new AppSettings
            {
                SettingsFilePath = settingsFilePath
            };
            ret.Save();
            return ret;
        }
    }
}