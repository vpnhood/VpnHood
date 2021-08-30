using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Common;

namespace VpnHood.Client.App
{
    public class AppSettings
    {
        [JsonIgnore] public string SettingsFilePath { get; private set; } = null!;

        public UserSettings UserSettings { get; set; } = new();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Token TestServerToken => Token.FromAccessKey(TestServerAccessKey);
        public string? TestServerTokenAutoAdded { get; set; }

        public string TestServerAccessKey =>
            "vh://eyJuYW1lIjoiUHVibGljIFNlcnZlciIsInYiOjIsInNpZCI6MTEsInRpZCI6IjEwNDczNTljLWExMDctNGU0OS04NDI1LWMwMDRjNDFmZmI4ZiIsInNlYyI6IlRmK1BpUTRaS1oyYW1WcXFPNFpzdGc9PSIsImlzdiI6ZmFsc2UsImhuYW1lIjoibW8uZ2l3b3d5dnkubmV0IiwiaHBvcnQiOjQ0MywiaGVwIjoiNTEuODEuODEuMjUwOjQ0MyIsImNoIjoiM2dYT0hlNWVjdWlDOXErc2JPN2hsTG9rUWJBPSIsInBiIjp0cnVlLCJ1cmwiOiJodHRwczovL3d3dy5kcm9wYm94LmNvbS9zL2htaGNoNmIwOXg3ZnV4My9wdWJsaWMuYWNjZXNza2V5P2RsPTEifQ==";

        public event EventHandler? OnSaved;

        public void Save()
        {
            var json = JsonSerializer.Serialize(this);
            File.WriteAllText(SettingsFilePath, json, Encoding.UTF8);
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
}