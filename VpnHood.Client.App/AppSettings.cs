using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace VpnHood.Client.App
{
    public class AppSettings
    {
        public string SettingsFilePath { get; private set; }
        public AppUserSettings UserSettings { get; set; } = new AppUserSettings();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid TestServerTokenId { get; set; } 
        public void Save()
        {
            var json = JsonSerializer.Serialize(this);
            File.WriteAllText(SettingsFilePath, json, Encoding.UTF8);
        }

        public string TestServerAccessKey = "eyJuYW1lIjoiUHVibGljIFRlc3QgU2VydmVyIiwidiI6MSwic2lkIjoxLCJ0aWQiOiJiYzNlZjY1ZC1mYTI4LTQ1OWEtODNhYi0xYWQ2MDgyYTk3MzAiLCJzZWMiOiJUaDJwUWlBRWJ1bXI1ZkZEbXUvd1VRPT0iLCJkbnMiOiJzaWdtYWxpYi5jb20iLCJwa2giOiJENjNVTFFRc1NXcmtaM2lQV3V1aFRRPT0iLCJlcCI6IjUxLjgxLjg0LjE0Mjo0NDMiLCJwYiI6dHJ1ZX0=";

        internal static AppSettings Load(string settingsFilePath)
        {
            try
            {
                var json = File.ReadAllText(settingsFilePath, Encoding.UTF8);
                var ret = JsonSerializer.Deserialize<AppSettings>(json);
                ret.SettingsFilePath = settingsFilePath;
                return ret;
            }
            catch
            {
                var ret = new AppSettings
                {
                    SettingsFilePath = settingsFilePath
                };
                return ret;
            }
        }
    }
}
