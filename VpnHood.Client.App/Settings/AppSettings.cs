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
                ? "vh://eyJuYW1lIjoiVnBuSG9vZCBQdWJsaWMgU2VydmVycyIsInYiOjEsInNpZCI6MTAwMSwidGlkIjoiNWFhY2VjNTUtNWNhYy00NTdhLWFjYWQtMzk3Njk2OTIzNmY4Iiwic2VjIjoiNXcraUhNZXcwQTAzZ3c0blNnRFAwZz09IiwiaXN2IjpmYWxzZSwiaG5hbWUiOiJtby5naXdvd3l2eS5uZXQiLCJocG9ydCI6NDQzLCJjaCI6IjNnWE9IZTVlY3VpQzlxK3NiTzdobExva1FiQT0iLCJwYiI6dHJ1ZSwidXJsIjoiaHR0cHM6Ly93d3cuZHJvcGJveC5jb20vcy82YWlrdHFmM2xhZW9vaGY/ZGw9MSIsImVwIjpbIjUxLjgxLjIxMC4xNjQ6NDQzIl19"
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

    public event EventHandler? OnSaved;
    private readonly object _saveLock = new();

    public void Save()
    {
        lock (_saveLock)
        {
            ConfigTime = DateTime.Now;
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json, Encoding.UTF8);
        }

        OnSaved?.Invoke(this, EventArgs.Empty);
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