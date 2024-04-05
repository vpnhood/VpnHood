using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Client.App.Settings;

public class AppSettings
{
    [JsonIgnore] 
    public string SettingsFilePath { get; private set; } = null!;
    
    // ReSharper disable StringLiteralTypo
    [JsonIgnore]
    public string PublicAccessKey
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var publicAccessKeyTag = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(attr => attr.Key == "PublicAccessKey")?.Value;
            return string.IsNullOrWhiteSpace(publicAccessKeyTag)
                ? "vh://eyJ2Ijo0LCJuYW1lIjoiVnBuSG9vZCBHbG9iYWwgU2VydmVycyIsInNpZCI6IjEwMTAiLCJ0aWQiOiI1YWFjZWM1NS01Y2FjLTQ1N2EtYWNhZC0zOTc2OTY5MjM2ZjgiLCJzZWMiOiI1dytpSE1ldzBBMDNndzRuU2dEUDBnPT0iLCJzZXIiOnsiY3QiOiIyMDI0LTA0LTA1VDA3OjI5OjI2WiIsImhuYW1lIjoibW8uZ2l3b3d5dnkubmV0IiwiaHBvcnQiOjAsImlzdiI6ZmFsc2UsInNlYyI6InZhQnFVOVJDM1FIYVc0eEY1aWJZRnc9PSIsImNoIjoiM2dYT0hlNWVjdWlDOXErc2JPN2hsTG9rUWJBPSIsInVybCI6Imh0dHBzOi8vcmF3LmdpdGh1YnVzZXJjb250ZW50LmNvbS92cG5ob29kL1Zwbkhvb2QuRmFybUtleXMvbWFpbi9GcmVlX2VuY3J5cHRlZF90b2tlbi50eHQiLCJlcCI6WyI1MS44MS44MS4yNTA6NDQzIiwiWzI2MDQ6MmRjMDoxMDE6MjAwOjo5M2VdOjQ0MyJdfX0="
                : publicAccessKeyTag;
        }
    }
    // ReSharper restore StringLiteralTypo

    public int Version { get; set; } = 1;
    public bool IsQuickLaunchAdded { get; set; } 
    public bool IsQuickLaunchRequested { get; set; }
    public DateTime ConfigTime { get; set; } = DateTime.Now;
    public UserSettings UserSettings { get; set; } = new();
    public Guid ClientId { get; set; } = Guid.NewGuid();
    public string? LastCountryIpGroupId { get; set; }
    public string? TestServerTokenAutoAdded { get; set; }
    public DateTime? LastUpdateCheckTime { get; set; }

    public event EventHandler? Saved;
    private readonly object _saveLock = new();

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