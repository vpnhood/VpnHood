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
                ? "vh://eyJuYW1lIjoiVnBuSG9vZCBHbG9iYWwgU2VydmVycyIsInYiOjMsInNpZCI6MTAxMCwidGlkIjoiNWFhY2VjNTUtNWNhYy00NTdhLWFjYWQtMzk3Njk2OTIzNmY4Iiwic2VjIjoiNXcraUhNZXcwQTAzZ3c0blNnRFAwZz09IiwiaXN2IjpmYWxzZSwiaG5hbWUiOiJtby5naXdvd3l2eS5uZXQiLCJocG9ydCI6MCwiY2giOiIzZ1hPSGU1ZWN1aUM5cStzYk83aGxMb2tRYkE9IiwicGIiOmZhbHNlLCJ1cmwiOm51bGwsImVwIjpbIjUxLjgxLjIxMC4xNjQ6NDQzIiwiWzI2MDQ6MmRjMDoyMDI6MzAwOjo1Y2VdOjQ0MyJdfQ=="
                : publicAccessKeyTag;
        }
    }
    // ReSharper restore StringLiteralTypo

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
                      throw new FormatException(
                          $"Could not deserialize {nameof(AppSettings)} from {settingsFilePath}");
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